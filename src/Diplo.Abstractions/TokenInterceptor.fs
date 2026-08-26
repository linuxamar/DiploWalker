module Diplo.Abstractions.TokenInterceptor

open Grpc.Core

/// Crée des CallCredentials qui ajoutent l'en-tête Authorization à partir du token d'authentification.
/// Utilisation avec Grpc.Net.Client.GrpcChannel :
///   let creds = TokenInterceptor.createTokenCredentials ()
///   let channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, creds)
///   let channel = GrpcChannel.ForAddress("http://localhost:5001", GrpcChannelCredentials = channelCredentials)
let createTokenCredentials () : CallCredentials =
    CallCredentials.FromInterceptor(fun _context metadata ->
        task {
            match AuthToken.loadToken () with
            | Some token -> metadata.Add("Authorization", sprintf "Bearer %s" token)
            | None -> ()
        })
