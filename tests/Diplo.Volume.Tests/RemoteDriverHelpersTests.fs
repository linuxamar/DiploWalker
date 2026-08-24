namespace Diplo.Volume.Tests

module RemoteDriverHelpersTests =

    open System
    open Xunit
    open FsUnit.Xunit
    open Grpc.Core
    open Diplo.Volume.Drivers

    [<Fact>]
    let ``unmountNfsLike propage RpcException si umount echoue`` () =
        let ex =
            Assert.Throws<RpcException>(fun () -> RemoteDriverHelpers.unmountNfsLike "/chemin/inexistant" |> ignore)

        ex.StatusCode |> should equal StatusCode.Internal

    [<Fact>]
    let ``unmountNfsLike propage RpcException si chemin inexistant`` () =
        let ex =
            Assert.Throws<RpcException>(fun () -> RemoteDriverHelpers.unmountNfsLike "/tmp/test-unmount" |> ignore)

        ex.StatusCode |> should equal StatusCode.Internal
