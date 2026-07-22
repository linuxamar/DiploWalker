namespace Diplo.Cli

open System
open Spectre.Console.Cli
open Microsoft.Extensions.DependencyInjection

type TypeRegistrar() =
    let services = ServiceCollection()
    do services.AddSingleton<Diplo.Core.Output.IOutputPort>(SpectreOutputPort()) |> ignore
    let mutable buildProvider: IServiceProvider option = None

    interface ITypeRegistrar with
        member _.Build() =
            let provider =
                match buildProvider with
                | Some p -> p
                | None ->
                    let p = services.BuildServiceProvider()
                    buildProvider <- Some p
                    p
            { new ITypeResolver with
                member _.Resolve(type') = provider.GetService(type') }

        member _.Register(serviceType, implType) =
            services.AddTransient(serviceType, implType) |> ignore

        member _.RegisterInstance(serviceType, instance) =
            services.AddSingleton(serviceType, instance) |> ignore

        member _.RegisterLazy(serviceType, factory) =
            services.AddSingleton(serviceType, Func<IServiceProvider, obj>(fun _ -> factory.Invoke())) |> ignore
