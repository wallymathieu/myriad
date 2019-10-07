﻿namespace Myriad
open System
open Fantomas
open System.IO
open Microsoft.FSharp.Compiler.Ast
open FsAst
open Argu

module Main =

    type Arguments =
        | [<Mandatory>] InputFile of string
        | Namespace of string
        | [<Mandatory>] OutputFile of string
        | Plugin of string
    with
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | InputFile _ -> "specify a file to use as input."
                | Namespace _ -> "specify a namespace to use."
                | OutputFile _ -> "Specify the file name that the generated code will be written to."
                | Plugin _ -> "Register an assembly plugin."

    [<EntryPoint>]
    let main argv =
        let parser = ArgumentParser.Create<Arguments>(programName = "myriad")

        try
            let results = parser.Parse argv
            let inputFile = results.GetResult InputFile
            let outputFile = results.GetResult OutputFile
            let namespace' =
                match results.TryGetResult Namespace with
                | Some ns -> ns
                | None -> Path.GetFileNameWithoutExtension(inputFile)
            let plugins = results.GetResults Plugin

#if DEBUG
            printfn "Plugins:"
            plugins |> List.iter (printfn "- '%s'")
#endif

            let findPlugins (path: string) =
                let assembly = System.Reflection.Assembly.LoadFrom(path)

                let gens =
                    [ for t in assembly.GetTypes() do
                        if t.GetCustomAttributes(typeof<Myriad.Core.MyriadSdkGeneratorAttribute>, true).Length > 0 then
                            yield t ]
                gens
            
            let generators =
                plugins
                |> List.collect findPlugins
                
#if DEBUG
            printfn "Generators:"
            generators |> List.iter (fun t -> printfn "- '%s'" t.FullName)
#endif
            
            let execGen namespace' parsedInput (genType: Type) =
                let instance = Activator.CreateInstance(genType) :?> Myriad.Core.IMyriadGen

#if DEBUG
                printfn "Executing: %s..." genType.FullName
#endif

                let result = instance.Generate(namespace', parsedInput)
#if DEBUG
                printfn "Result: '%A'" result
#endif
                result
#if DEBUG
            printfn "Exec generators:"
#endif
            let ast = Myriad.Core.Ast.getAst inputFile
            
            let generated =
                generators
                |> List.map (execGen namespace' ast)

            let parseTree =
                ParsedInput.CreateImplFile(
                    ParsedImplFileInputRcd.CreateFs(outputFile)
                        .AddModules generated)


            let formattedCode = formatAst parseTree

            let code =
                [   "//------------------------------------------------------------------------------"
                    "//        This code was generated by myriad."
                    "//        Changes to this file will be lost when the code is regenerated."
                    "//------------------------------------------------------------------------------"
                    formattedCode ]
                |> String.concat Environment.NewLine

            File.WriteAllText(outputFile, code)

            printfn "%A" code
#if DEBUG
            printfn "AST-----------------------------------------------"
            printfn "%A" parseTree
#endif
            0 // return an integer exit code

        with
        | :? ArguParseException as ae ->
            printfn "%s" ae.Message
            match ae.ErrorCode with
            | Argu.ErrorCode.HelpText -> 0
            | _ -> 2
        | :? ArguParseException as ae when ae.ErrorCode = Argu.ErrorCode.HelpText ->
            printfn "%s" ae.Message
            3
        | :? FileNotFoundException as fnf ->
            printfn "ERROR: inputfile %s doesn not exist\n%s" fnf.FileName (parser.PrintUsage())
            4