namespace Aardvark_test

open System
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.UI
open Aardvark.UI.Primitives
open Aardvark.Base.Rendering
open Aardvark_test.Model

type Message =
    | CameraMessage of FreeFlyController.Message
    | LightMessage of int * lightControl.Message

module App =   


    let cameraConfig  =  {FreeFlyController.initial.freeFlyConfig with zoomMouseWheelSensitivity = 0.5} 
    let initialView = CameraView.lookAt (V3d(2.0, 2.0, -3.0)) (V3d(0.0, 1.0, 0.0)) (V3d.OIO * 1.0)
    let initial = { 
        light =  light.defaultLight
        cameraState = {FreeFlyController.initial  with freeFlyConfig = cameraConfig; view = initialView}
        lights = HSet.ofList [{index  = 0; light = PointLight  {lightPosition = V4d(0.0,1.5,-0.5,1.0); color = C3d.Red; attenuationQad = 1.0; attenuationLinear = 0.0; intensity = 1.0}}; 
            {index  = 1; light = PointLight  {lightPosition = V4d(-1.0,1.5,-0.0,1.0); color = C3d.Green; attenuationQad = 1.0; attenuationLinear = 0.0; intensity = 1.0}}]
        currentLightIndex  = 0
    }

    let update (m : Model) (msg : Message) =
        //compose the update functions from the updates of the sub-model
        Log.warn "%A" msg
        match msg with
        | CameraMessage msg ->
            { m with cameraState = FreeFlyController.update m.cameraState msg }
        | LightMessage (i, lms) -> 
            let li' =  m.lights |> Seq.filter (fun li -> li.index = i) |> Seq.first
            Log.warn "%A" li'
            match  li' with 
            |Some li ->
                let l = lightControl.update li.light lms
                Log.warn "%A" l
                { m with lights = HSet.add {li with light = l} m.lights }
            |None ->  m

    
    let figureMesh =
        Aardvark.SceneGraph.IO.Loader.Assimp.load @"..\..\..\data\SLE_Gnom3.obj"
        |> Sg.adapter
        //|> Sg.transform (Trafo3d.FromOrthoNormalBasis(V3d.IOO, V3d.OOI, -V3d.OIO))
        |> Sg.transform (Trafo3d.Scale(1.0,1.0,1.0))
    
    let uniformLight (l : IMod<MLight>) =
        //needs to be adaptive because the  Light can change and is an IMod
        //we go from IMod<MLight> to IMod<ISg<Message>>
        adaptive {
            let! d = l
            match d with
            | MDirectionalLight  x' ->
               let! x  = x'
                //Map to a type more convinient in the shaders
               return SLEUniform.DirectionalLight {lightDirection = x.lightDirection; color = x.color.ToV3d() * x.intensity}
            | MPointLight  x' ->
                let! x  = x'
                return SLEUniform.PointLight {lightPosition = x.lightPosition; color = x.color.ToV3d() * x.intensity; attenuationQad = x.attenuationQad; attenuationLinear = x.attenuationLinear}
        } 

    let uniformLights (lights : aset<MIndexedLight>) (m :ISg<Message>)  =
        let numLights = ASet.count lights
        let a =  Array.init 10 (fun _ -> SLEUniform.NoLight )
        let u = aset{
                for l  in  lights do
                    let! l = uniformLight l.light
                    yield l
                }
                |> ASet.fold (fun ((i : int), (a : SLEUniform.Light [])) l -> a.[i] <- l; (i+1, a)) (0,a)
                |> Mod.map (fun (i, a) -> a)
                |> Sg.uniform "Lights" 
        m
        |> u
        |> Sg.uniform "NumLights" numLights

    let lightSourceModels (lights : aset<MIndexedLight> ) =
        aset {
            for l in  lights do
                let! l' = l.light
                let  m = 
                    match l' with
                    | MDirectionalLight ld -> Sg.empty
                    | MPointLight lp -> 
                        Sg.sphere 6 (Mod.map ( fun v -> v.color.ToC4b()) lp ) (Mod.constant 0.03) 
                        |> Sg.translate' (Mod.map ( fun v -> v.lightPosition.XYZ) lp)
                yield m 
        } 
        |> Sg.set
        //simpel shader independend of light 
        |> Sg.shader {
            do! DefaultSurfaces.trafo
            do! DefaultSurfaces.vertexColor 
            }   

    //the 3D scene and control
    let view3D (m : MModel) =
        let frustum = 
            Frustum.perspective 30.0 0.1 100.0 1.0 
                |> Mod.constant

        let sg =
            figureMesh
            |> uniformLights m.lights
            |> Sg.shader {
                do! DefaultSurfaces.trafo
                do! DefaultSurfaces.vertexColor
                do! DefaultSurfaces.diffuseTexture 
                do! SLESurfaces.lighting false
                }
            |> Sg.andAlso <| lightSourceModels m.lights

        let att =
            [
                style "position: fixed; left: 0; top: 0; width: 100%; height: 100%"
                attribute "showFPS" "true"
               // attribute "data-renderalways" "1"
            ]

        FreeFlyController.controlledControl m.cameraState CameraMessage frustum (AttributeMap.ofList att) sg
        
    // main view for UI and  
    let view (m : MModel) =
        require Html.semui ( // we use semantic ui for our gui. the require function loads semui stuff such as stylesheets and scripts
            body [] (        // explit html body for our app (adorner menus need to be immediate children of body). if there is no explicit body the we would automatically generate a body for you.
                Html.SemUi.adornerMenu [ 
                "Change Light",
                    aset {
                        for li in m.lights do
                            let! i = li.index
                            let! l = li.light    
                            let name = sprintf "Light %i" i
                            yield Html.SemUi.accordion name "" false [lightControl.view  l |> UI.map (fun msg -> LightMessage (i, msg))]
                    } 
                    |>  ASet.toList
                ] [view3D m]
            )
        )

    let app =
        {
            initial = initial
            update = update
            view = view
            threads = Model.Lens.cameraState.Get >> FreeFlyController.threads >> ThreadPool.map CameraMessage
            unpersist = Unpersist.instance
        }