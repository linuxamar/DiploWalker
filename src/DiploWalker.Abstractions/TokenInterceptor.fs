module DiploWalker.Abstractions.TokenInterceptor

open Grpc.Core

/// Crée des CallCredentials qui ajoutent l'en-tête Authorization à partir du token d'authentification.
/// Utilisation avec Grpc.Net.Client.GrpcChannel :
///   let creds = TokenInterceptor.createTokenCredentials ()
///   let channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, creds)
///   let port = if DEBUG then 5001 else 6001 (cf. DiploWalkerPorts.Container)
///   let channel = GrpcChannel.ForAddress(sprintf "http://localhost:%d" port, GrpcChannelCredentials = channelCredentials)
let createTokenCredentials () : CallCredentials =
    CallCredentials.FromInterceptor(fun _context metadata ->
        task {
            match AuthToken.loadToken () with
            | Some token -> metadata.Add("Authorization", sprintf "Bearer %s" token)
            | None -> ()
        })


