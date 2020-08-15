// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

//#I "packages/FSharp.Compiler.Tools/tools/"
#r @"packages/FAKE/tools/FakeLib.dll"
#r @"packages/Octokit/lib/net46/Octokit.dll"

open Fake.DotNet
open Fake.Core
open Fake.Core.TargetOperators
open Fake.Tools
open Fake.IO
open Fake.IO.GlobbingPattern
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Api
open System
open System.IO
open Octokit
#if MONO
#else
//#load "packages/SourceLink.Fake/tools/Fake.fsx"
//open SourceLink
#endif

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "DynamicsCRMProvider"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "A type provider for Microsoft Dynamics CRM 2011."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "A type provider for Microsoft Dynamics CRM 2011."

// List of author names (for NuGet package)
let authors = [ "Ross McKinlay; Steffen Forkmann; Sergey Tihon" ]

// Tags for your project (for NuGet package)
let tags = "F# fsharp typeproviders dynamics CRM"

// File system information 
let solutionFile  = "DynamicsCRMProvider.sln"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "fsprojects" 
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "DynamicsCRMProvider"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.github.com/fsprojects"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|) (projFileName:string) = 
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title (projectName)
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath: string)
        ( projectPath, 
          projectName,
          System.IO.Path.GetDirectoryName(projectPath: string),
          (getAssemblyInfoAttributes projectName)
        )
        
    AssemblyInfoFile.createFSharp (("src" @@ "Common") @@ "AssemblyInfo.fs") 
        [ AssemblyInfo.Title "DynamicsCRMProvider"
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    !! "src/**/*.csproj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        match projFileName with
        | Fsproj -> AssemblyInfoFile.createFSharp (("src" @@ folderName) @@ "AssemblyInfo.fs") attributes
        | Csproj -> AssemblyInfoFile.createCSharp ((folderName @@ "Properties") @@ "AssemblyInfo.cs") attributes
        | Vbproj -> AssemblyInfoFile.createVisualBasic ((folderName @@ "My Project") @@ "AssemblyInfo.vb") attributes
        )
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the 
// src folder to support multiple project outputs
Target.create "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin/Release", "bin/DynamicsCRMProvider"))
    |>  Seq.iter (fun (fromDir, toDir) -> Shell.copyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

Target.create "CleanDocs" (fun _ ->
    Shell.cleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project
let toolPath = Environment.environVarOrDefault "ToolPath"

let buildMode = Environment.environVarOrDefault "buildMode" "Release"
let setParams (defaults:MSBuildParams) =
        { defaults with
            Properties =
                [
                    "VisualStudioVersion", "16.0"
                    "Optimize", "True"
                    "Configuration", buildMode
                ]
         }
         
Target.create "Build" (fun _ ->
    !! solutionFile
    |> MSBuild.runRelease setParams "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "RunTests" (fun _ ->
    !! testAssemblies
    |> Testing.NUnit3.run (fun p ->
        { p with
            ShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputDir = "TestResults.xml" })
)

#if MONO
#else
// --------------------------------------------------------------------------------------
// SourceLink allows Source Indexing on the PDB generated by the compiler, this allows
// the ability to step through the source code of external libraries https://github.com/ctaggart/SourceLink

//Target.create "SourceLink" (fun _ ->
//    let baseUrl = sprintf "%s/%s/{0}/%%var2%%" gitRaw (project.ToLower())
//    use repo = new GitRepo(__SOURCE_DIRECTORY__)
//    
//    let addAssemblyInfo (projFileName:String) = 
//        match projFileName with
//        | Fsproj -> (projFileName, "**/AssemblyInfo.fs")
//        | Csproj -> (projFileName, "**/AssemblyInfo.cs")
//        | Vbproj -> (projFileName, "**/AssemblyInfo.vb")
//        
//    !! "src/**/*.??proj"
//    |> Seq.map addAssemblyInfo
//    |> Seq.iter (fun (projFile, assemblyInfo) ->
//        let proj = VsProj.LoadRelease projFile 
//        Trace.logfn "source linking %s" proj.OutputFilePdb
//        let files = proj.Compiles -- assemblyInfo
//        repo.VerifyChecksums files
//        proj.VerifyPdbChecksums files
//        proj.CreateSrcSrv baseUrl repo.Revision (repo.Paths files)
//        Pdbstr.exec proj.OutputFilePdb proj.OutputFilePdbSrcSrv
//    )
//)

#endif

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    Paket.pack(fun p -> 
        { p with
            OutputPath = "bin"
            Version = release.NugetVersion
            ReleaseNotes = String.toLines release.Notes})
)

Target.create "PublishNuget" (fun _ ->
    Paket.push(fun p ->
        { p with
            WorkingDir = "bin" })
)


// --------------------------------------------------------------------------------------
// Generate the documentation
let fsiExe = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\community\Common7\IDE\CommonExtensions\Microsoft\FSharp\"
let executeFSIWithArgs workingDir script scriptArgs =
    System.Convert.ToBoolean(Fsi.exec (fun p -> 
            { p with 
                TargetProfile = Fsi.Profile.NetStandard
                WorkingDirectory = workingDir
                ToolPath = Fsi.FsiTool.External fsiExe
            }
        ) script scriptArgs)

Target.create "GenerateReferenceDocs" (fun _ ->
    if not <| executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE"] then
      failwith "generating reference documentation failed"
)

let generateHelp' fail debug =
    let args =
        if debug then ["--define:HELP"]
        else ["--define:RELEASE"; "--define:HELP"]
    if executeFSIWithArgs "docs/tools" "generate.fsx" args then
        Trace.traceImportant "Help generated"
    else
        if fail then
            failwith "generating help documentation failed"
        else
            Trace.traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target.create "GenerateHelp" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp true
)

Target.create "GenerateHelpDebug" (fun _ ->
    File.delete "docs/content/release-notes.md"
    Shell.copyFile "docs/content/" "RELEASE_NOTES.md"
    Shell.rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    File.delete "docs/content/license.md"
    Shell.copyFile "docs/content/" "LICENSE.txt"
    Shell.rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp' true true
)

Target.create "KeepRunning" (fun _ ->    
    use watcher = new FileSystemWatcher(DirectoryInfo("docs/content").FullName,"*.*")
    watcher.EnableRaisingEvents <- true
    watcher.Changed.Add(fun e -> generateHelp false)
    watcher.Created.Add(fun e -> generateHelp false)
    watcher.Renamed.Add(fun e -> generateHelp false)
    watcher.Deleted.Add(fun e -> generateHelp false)

    Trace.traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.EnableRaisingEvents <- false
    watcher.Dispose()
)


Target.create "GenerateDocs" ignore

let createIndexFsx lang =
    let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
F# Project Scaffold ({0})
=========================
*)
"""
    let targetDir = "docs/content" @@ lang
    let targetFile = targetDir @@ "index.fsx"
    Directory.ensure targetDir
    System.IO.File.WriteAllText(targetFile, System.String.Format(content, lang))

Target.create "AddLangDocs" (fun _ ->
    let args = System.Environment.GetCommandLineArgs()
    if args.Length < 4 then
        failwith "Language not specified."

    args.[3..]
    |> Seq.iter (fun lang ->
        if lang.Length <> 2 && lang.Length <> 3 then
            failwithf "Language must be 2 or 3 characters (ex. 'de', 'fr', 'ja', 'gsw', etc.): %s" lang

        let templateFileName = "template.cshtml"
        let templateDir = "docs/tools/templates"
        let langTemplateDir = templateDir @@ lang
        let langTemplateFileName = langTemplateDir @@ templateFileName

        if System.IO.File.Exists(langTemplateFileName) then
            failwithf "Documents for specified language '%s' have already been added." lang

        Directory.ensure langTemplateDir
        Shell.copy langTemplateDir [ templateDir @@ templateFileName ]

        createIndexFsx lang)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target.create "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    Shell.cleanDir tempDocsDir
    Git.Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    Shell.copyRecursive "docs/output" tempDocsDir true |> Trace.tracefn "%A"
    Git.Staging.stageAll tempDocsDir
    Git.Commit.exec tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Git.Branches.push tempDocsDir
)

//#load "paket-files/FoothillSolutions/FAKE/modules/Octokit/Octokit.fsx"
//open Octokit

Target.create "Release" (fun _ ->
    Git.Staging.stageAll ""
    Git.Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Git.Branches.push ""

    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" "origin" release.NugetVersion
    
    // release on github
    let token = 
        match Environment.environVarOrDefault "github_token" "" with 
        | s when not (System.String.IsNullOrWhiteSpace s) -> s
        |_ -> failwith "please set the github_token environment variable to a github personal access token with repro access."
    GitHub.createClientWithToken token
     |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
     
     // TODO: |> uploadFile "PATH_TO_FILE"    
     //|> Release 
     |> Async.RunSynchronously
     |>ignore
)

Target.create "BuildPackage" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
//  ==> "RunTests"
//  =?> ("GenerateReferenceDocs",BuildServer.isLocalBuild)
//  =?> ("GenerateDocs",BuildServer.isLocalBuild)
  ==> "All"
//  =?> ("ReleaseDocs",BuildServer.isLocalBuild)

"All" 
#if MONO
#else
//  =?> ("SourceLink", Pdbstr.tryFind().IsSome )
#endif
  ==> "NuGet"
  ==> "BuildPackage"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"

"CleanDocs"
  ==> "GenerateHelpDebug"

"GenerateHelp"
  ==> "KeepRunning"
    
"ReleaseDocs"
  ==> "Release"

"BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

Target.runOrDefault "All"
