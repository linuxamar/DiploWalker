namespace Diplo.Container.Tests

module Placeholder =

    open Xunit
    open FsUnit.Xunit

    [<Fact>]
    let ``Le projet Container.Tests compile`` () =
        1 + 1 |> should equal 2
