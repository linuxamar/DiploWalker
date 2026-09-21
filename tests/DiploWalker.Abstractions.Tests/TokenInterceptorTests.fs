namespace DiploWalker.Abstractions.Tests

module TokenInterceptorTests =

    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions

    [<Fact>]
    let ``createTokenCredentials retourne un objet CallCredentials non null`` () =
        let creds = TokenInterceptor.createTokenCredentials ()
        creds |> should not' (be Null)

    [<Fact>]
    let ``createTokenCredentials retourne toujours le meme type`` () =
        let creds1 = TokenInterceptor.createTokenCredentials ()
        let creds2 = TokenInterceptor.createTokenCredentials ()
        creds1.GetType() |> should equal (creds2.GetType())

