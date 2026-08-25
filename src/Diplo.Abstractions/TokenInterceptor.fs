module Diplo.Abstractions.TokenInterceptor

open Grpc.Core

/// Crée des CallCredentials qui ajoutent l'en-tête Authorization à partir du token d'authentification.
/// Utilisation avec Grpc.Net.Client.GrpcChannel :
///   let channel = GrpcChannel.ForAddress("http://localhost:5001")
///   let invoker = channel.Intercept(TokenInterceptor.createTokenCredentials())
let createTokenCredentials () : CallCredentials =
    CallCredentials.FromInterceptor(fun _context metadata ->
        task {
            match AuthToken.loadToken () with
            | Some token -> metadata.Add("Authorization", sprintf "Bearer %s" token)
            | None -> ()
        })
