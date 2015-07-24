// Consult http://fsharp.github.io/FAKE/apidocs/
// and http://fsharp.github.io/FAKE/ for details.

#r @"packages/FAKE/tools/FakeLib.dll"

open Fake.AssemblyInfoFile
open Fake

let solutionFiles  = !! "*.sln"

let setParams targets config defaults =
    { defaults with
        Verbosity = Some(Normal)
        Targets = targets
        NodeReuse = false
        Properties =
            [
                "Configuration", config
                "Platform", "Any CPU"
                "RestorePackages", "True"
            ]
     }

Target "Clean" (fun _ ->
    solutionFiles
    |> Seq.iter(fun p -> 
            build (setParams ["Clean"] "Release") p |> ignore 
            build (setParams ["Clean"] "Debug") p |> ignore 
    )
)

let version = "0.1"

let createAssembly () = 
    CreateFSharpAssemblyInfo "./BackupDBToAzure/AssemblyInfo.fs"
            [ Attribute.Title "BackupDBToAzure"
              Attribute.Description "BackupDBToAzure"
              Attribute.Configuration "BackupDBToAzure"
              Attribute.Guid "2D2971A2-3B9C-4EB9-A71B-9EF73F756CE9"
              Attribute.Product "BackupDBToAzure"
              Attribute.ComVisible false
              //Attribute.Company ""
              //Attribute.Trademark ""
              //Attribute.Copyright ""
              Attribute.Version version
              Attribute.FileVersion version]

Target "BuildRelease" (fun _ -> 
    solutionFiles 
    |> Seq.iter(fun p -> 
        createAssembly ()
        build (setParams ["Rebuild"] "Release") p 
    )
)

Target "BuildDebug" (fun _ -> 
    solutionFiles 
    |> Seq.iter(fun p -> 
        createAssembly()
        build (setParams ["Rebuild"] "Debug") p 
    )
)

Target "All" DoNothing

"Clean"
  ==> "BuildDebug"
  ==> "All"

RunTargetOrDefault "All"
