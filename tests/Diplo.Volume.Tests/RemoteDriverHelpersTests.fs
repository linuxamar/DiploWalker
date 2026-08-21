namespace Diplo.Volume.Tests

module RemoteDriverHelpersTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Diplo.Volume.Drivers

    [<Fact>]
    let ``unmountNfsLike ne propage pas l'exception si umount echoue`` () =
        RemoteDriverHelpers.unmountNfsLike "/chemin/inexistant"
        // ne doit pas lever d'exception (silencieux)

    [<Fact>]
    let ``unmountNfsLike retourne unit`` () =
        let result = RemoteDriverHelpers.unmountNfsLike "/tmp/test-unmount"
        result |> should equal ()
