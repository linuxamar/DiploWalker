namespace DiploWalker.Cli

open System
open Spectre.Console.Cli
open Microsoft.Extensions.DependencyInjection

type TypeRegistrar() =
    let services = ServiceCollection()

    do
        services.AddSingleton<DiploWalker.Core.Output.IOutputPort>(SpectreOutputPort())
        |> ignore

    do
        services.AddSingleton<DiploWalker.Core.Clients.IDiploClients>(DiploWalker.Core.Clients.DiploWalkerClients())
        |> ignore

    let buildProvider = lazy (services.BuildServiceProvider() :> IServiceProvider)

    interface ITypeRegistrar with
        member _.Build() =
            let provider = buildProvider.Value

            { new ITypeResolver with
                member _.Resolve(type') = provider.GetService(type') }

        member _.Register(serviceType, implType) =
            services.AddTransient(serviceType, implType) |> ignore

        member _.RegisterInstance(serviceType, instance) =
            services.AddSingleton(serviceType, instance) |> ignore

        member _.RegisterLazy(serviceType, factory) =
            services.AddSingleton(serviceType, Func<IServiceProvider, obj>(fun _ -> factory.Invoke()))
            |> ignore



