namespace Diplo.Container.Tests

module RegistryAuthTests =

    open System
    open System.IO
    open System.Text.Json
    open Xunit
    open FsUnit.Xunit
    open Diplo.Container

    let shouldContain (substring: string) (text: string) =
        Assert.Contains(substring, text)

    let shouldNotContain (substring: string) (text: string) =
        Assert.DoesNotContain(substring, text)

    /// Crée un fichier d'état temporaire isolé pour chaque test.
    let private withStateFile (test: string -> unit) =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-registry-tests-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory dir |> ignore
        let path = Path.Combine(dir, "registry-auth.json")
        try
            test path
        finally
            try Directory.Delete(dir, true) with _ -> ()

    [<Fact>]
    let ``add persist l'identifiant et le chiffre (pas de mot de passe en clair)`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"
            let json = File.ReadAllText path
            json |> shouldContain "myregistry.azurecr.io"
            json |> shouldContain "user"
            json |> shouldNotContain "secret")

    [<Fact>]
    let ``tryGetUserArg retourne user:password apres add`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"
            RegistryAuth.tryGetUserArg path "myregistry.azurecr.io"
            |> should equal (Some "user:secret"))

    [<Fact>]
    let ``remove supprime l'identifiant`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"
            RegistryAuth.remove path "myregistry.azurecr.io"
            RegistryAuth.tryGetUserArg path "myregistry.azurecr.io"
            |> should equal None)

    [<Fact>]
    let ``load retourne un etat vide si le fichier n'existe pas`` () =
        withStateFile (fun path ->
            RegistryAuth.load path |> should be Empty)

    [<Fact>]
    let ``add remplace l'identifiant d'un meme registre`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "reg.example.com" "user1" "pass1"
            RegistryAuth.add path "reg.example.com" "user2" "pass2"
            RegistryAuth.tryGetUserArg path "reg.example.com"
            |> should equal (Some "user2:pass2"))
