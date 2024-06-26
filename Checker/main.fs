﻿open OpenTK.Graphics.OpenGL
open Options.Globals
open System
open System.Diagnostics
open System.IO

open Argu
open System.Text.RegularExpressions

type CliArguments =
    | Update_Golden
    | Skip_GLSL_Compile
    | Skip_Performance_Tests

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Update_Golden -> "Update the golden tests"
            | Skip_GLSL_Compile -> "Skip the GLSL compilation of shaders"
            | Skip_Performance_Tests -> "Skip the tests of performance"

let cliArgs = ArgumentParser.Create<CliArguments>().ParseCommandLine()
//let cliArgs = ArgumentParser.Create<CliArguments>().ParseCommandLine([|"--update-golden"|])

let initOpenTK () =
    // OpenTK requires a GameWindow
    if not (cliArgs.Contains(Skip_GLSL_Compile)) then
        new OpenTK.GameWindow() |> ignore

// Return true if the file can be compiled as a GLSL shader.
let canBeCompiled content =
    if cliArgs.Contains(Skip_GLSL_Compile) then
        true
    else
        let fragmentShader = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fragmentShader, content)
        GL.CompileShader(fragmentShader)
        let info = GL.GetShaderInfoLog(fragmentShader)
        GL.DeleteShader(fragmentShader)
        if info = "" then
            true
        else
            printfn "compilation failed: %s" info
            false

let doMinify file content =
    let arr = ShaderMinifier.minify [|file, content|] |> fst |> Array.map (fun s -> s.code)
    Printer.print arr.[0]

let testMinifyAndCompile (file: string) =
    try
        let content = File.ReadAllText file
        if not (canBeCompiled content) then
            printfn "Invalid input file '%s'" file
            false
        else
            let minified = doMinify file content + "\n"
            if not (canBeCompiled minified) then
                printfn "Minification broke the file '%s'" file
                printfn "%s" minified
                false
            else
                printfn "Success: %s" file
                true
    with :? IO.FileNotFoundException as e ->
        printfn "%A" e
        false

let testPerformance files =
    printfn "Running performance tests..."
    let contents = files |> Array.map File.ReadAllText
    let stopwatch = Stopwatch.StartNew()
    for str in contents do
        doMinify "perf test" str |> ignore<string>
    let time = stopwatch.Elapsed
    printfn "%i files minified in %f seconds." files.Length time.TotalSeconds

// Generated files may contain the Shader Minifier version.
// Ignore version changes in the tests.
let versionRegex = new Regex(@"\bShader Minifier \d(\.\d+)+");

let runCommand argv =
    let cleanString (s: string) =
        let s = s.Replace("\r\n", "\n").Trim()
        versionRegex.Replace(s, "")
    Options.init argv
    let expected =
        try File.ReadAllText options.outputName |> cleanString
        with _ when cliArgs.Contains(Update_Golden) -> ""
           | _ -> reraise ()
    let result =
        use out = new StringWriter()
        let shaders, exportedNames = ShaderMinifier.minifyFiles options.filenames
        Formatter.print out shaders exportedNames options.outputFormat
        out.ToString() |> cleanString
    if result = expected then
        printfn "Success: %s" options.outputName
        0
    else
        printfn "Fail: %A" argv
        if cliArgs.Contains(Update_Golden) then
            File.WriteAllText(options.outputName, result + "\n")
        else
            printfn "Got %d: %A" result.Length result
            printfn "Expected %d: %A" expected.Length expected
        1

let testGolden () =
    let commands = File.ReadAllLines "tests/commands.txt" |> Array.choose (fun line ->
        let line = line.Trim()
        if line.Length = 0 || line.[0] = '#' then 
            None
        else
            Some (line.Split([|' '|]))
    )
    commands |> Array.sumBy runCommand

[<EntryPoint>]
let main argv =
    // Manually run compression tests by enabling this line:
    // CompressionTests.run ()

    initOpenTK()
    let mutable failures = testGolden()
    Options.init([|"--format"; "text"; "fake.frag"|])
    let unitTests = Directory.GetFiles("tests/unit", "*.frag")
    let realTests = Directory.GetFiles("tests/real", "*.frag");
    for f in unitTests do
        if not (testMinifyAndCompile f) then
            failures <- failures + 1
    testPerformance (Seq.concat [realTests; unitTests] |> Seq.toArray)
    if failures = 0 then
        printfn "All good."
    else
        printfn "%d failures." failures
    
    //System.Console.ReadLine() |> ignore
    if failures = 0 then 0 else 1
