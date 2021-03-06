open Aardvark_test

open Aardium
open Aardvark.Application.Slim
open Aardvark.Service
open Aardvark.UI
open Suave
open Suave.WebPart
open Aardvark.Rendering.Vulkan
open Aardvark.Base
open System

[<EntryPoint>]
let main args =
    Ag.initialize()
    Aardvark.Init()
    Aardium.init()

    let vapp = new OpenGlApplication()//new HeadlessVulkanApplication()

    let runtime = vapp.Runtime :> IRuntime

    let app = App.app runtime //inject runtime into app

    WebPart.startServer 4321 [
        MutableApp.toWebPart' runtime false (App.start app)
        Reflection.assemblyWebPart (System.Reflection.Assembly.GetEntryAssembly())
        //requiered to load the spectrum.js for the colorpicker (EmbeddedResources is the marker type to find the correct assembly)
        Reflection.assemblyWebPart typeof<Aardvark.UI.Primitives.EmbeddedResources>.Assembly
    ] |> ignore
    
    Aardium.run {
        title "Aardvark rocks \\o/"
        width 1600
        height 1000
        url "http://localhost:4321/"
    }

    0
