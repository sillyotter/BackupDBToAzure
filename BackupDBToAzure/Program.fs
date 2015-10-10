open FSharp.Configuration
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Blob
open System
open System.Data
open System.Configuration
open System.Data.SqlClient
open System.Text.RegularExpressions

type Settings = AppSettings< "App.config" >

let possiblyCreateCredentials constr credName key account =
    async {
        use con = new SqlConnection(constr)
        do! con.OpenAsync() |> Async.AwaitTask
        use cmd = con.CreateCommand()
        cmd.CommandType <- CommandType.Text
        cmd.CommandText <- @"
            IF NOT EXISTS(SELECT * FROM sys.credentials WHERE name = '" + credName + @"')
            CREATE CREDENTIAL " + credName + @" WITH IDENTITY = '" + account + "',
            SECRET = '" + key + "'"
        do! cmd.ExecuteNonQueryAsync()
            |> Async.AwaitTask
            |> Async.Ignore
        con.Close()
    }

let backupDatabase container (csb : SqlConnectionStringBuilder) credName =
    async {
        let targetUrl = Uri(Settings.TargetUrl, (sprintf "%s/%s-%s.bak" container csb.InitialCatalog (DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss"))))
        use con = new SqlConnection(csb.ConnectionString)
        do! con.OpenAsync() |> Async.AwaitTask
        use cmd = con.CreateCommand()
        cmd.CommandType <- CommandType.Text
        cmd.CommandTimeout <- int (TimeSpan.FromHours(6.0).TotalSeconds)
        cmd.CommandText <- @"
            BACKUP DATABASE " + csb.InitialCatalog + @"
            TO URL = '" + (targetUrl.ToString()) + @"'
            WITH CREDENTIAL = '" + credName + "'"
        do! cmd.ExecuteNonQueryAsync()
            |> Async.AwaitTask
            |> Async.Ignore
        con.Close()
    }

let getTargetConnectionStrings prefix =
    let csettings = ConfigurationManager.ConnectionStrings
    let cstrings = Array.create<ConfigurationElement> csettings.Count null
    csettings.CopyTo(cstrings, 0)
    cstrings
    |> Array.choose (function
           | :? ConnectionStringSettings as cs -> Some(cs)
           | _ -> None)
    |> Array.choose (fun x ->
           if x.Name.StartsWith(prefix) then Some(x.ConnectionString)
           else None)
    |> Array.map (fun x -> SqlConnectionStringBuilder(x))

[<EntryPoint>]
let main _ =
    if Environment.UserInteractive then
        let cct = new NLog.Targets.ColoredConsoleTarget(Layout = NLog.Layouts.Layout.FromString @"${date:format=yyyy-MM-ddTHH\:mm\:ss.fff}|${level}|${logger}|${message}|${exception:innerFormat=ShortType,Method,Message:maxInnerExceptionLevel=3:innerExceptionSeparator=^:separator=#:format=ShortType,Method,Message}")
        let rule1 = new NLog.Config.LoggingRule("*", NLog.LogLevel.Debug, cct)
        NLog.LogManager.Configuration.AddTarget("console", cct)
        NLog.LogManager.Configuration.LoggingRules.Add rule1
        NLog.LogManager.ReconfigExistingLoggers()

    let logger = NLog.FSharp.Logger()

    try
        let azureConStr = Settings.ConnectionStrings.AzureStorage
        let matchRegexString = Settings.MatchRegex
        let matchRegex = new Regex(matchRegexString, RegexOptions.Compiled)
        let age = Settings.Age
        let containerName = Settings.Container
        let credName = Settings.CredentialsName
        let targets = getTargetConnectionStrings "Target"

        let mastersOfEachServer =
            targets
            |> Array.groupBy (fun x -> x.DataSource)
            |> Array.map (fun (_, cs) -> cs.[0])
            |> Array.map (fun c ->
                   let ncs = SqlConnectionStringBuilder(c.ConnectionString)
                   ncs.InitialCatalog <- "master"
                   ncs)

        let storageAccount = CloudStorageAccount.Parse(azureConStr)

        mastersOfEachServer
        |> Array.map (fun x ->
               logger.Info "Creating credentials on server %s if needed." x.DataSource
               possiblyCreateCredentials x.ConnectionString credName (storageAccount.Credentials.ExportBase64EncodedKey()) storageAccount.Credentials.AccountName)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

        targets
        |> Array.map (fun x ->
               logger.Info "Backing up %s." x.InitialCatalog
               backupDatabase containerName x credName)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

        let blobClient = storageAccount.CreateCloudBlobClient()
        let container = blobClient.GetContainerReference(containerName)

        container.ListBlobs(useFlatBlobListing = true)
        |> Seq.choose (function
               | :? CloudPageBlob as pb -> Some(pb)
               | _ -> None)
        |> Seq.filter (fun x -> matchRegex.IsMatch(x.Name) && (x.Properties.LastModified.Value < (DateTimeOffset.Now - age)))
        |> Seq.map (fun x ->
               async {
                   logger.Info "Deleting old backup %s created at %s" x.Name (x.Properties.LastModified.Value.ToString())
                   do! x.DeleteAsync() |> Async.AwaitTask
               })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

    with ex -> logger.ErrorException ex "Failed to backup"
    0
