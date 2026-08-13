namespace Diplo.Core.Tests

module CommandLineTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Core.CommandLine

    [<Fact>]
    let ``split decoupe sur les espaces`` () =
        split "cmd /c echo bonjour" |> should equal [ "cmd"; "/c"; "echo"; "bonjour" ]

    [<Fact>]
    let ``split ignore les espaces multiples et les tabulations`` () =
        split "  cmd   /c  echo " |> should equal [ "cmd"; "/c"; "echo" ]

    [<Fact>]
    let ``split preserve les espaces entre guillemets`` () =
        split "cmd /c \"echo bonjour le monde\"" |> should equal [ "cmd"; "/c"; "echo bonjour le monde" ]

    [<Fact>]
    let ``split retire les guillemets`` () =
        split "exec \"C:\\Program Files\\app.exe\" --flag" |> should equal [ "exec"; "C:\\Program Files\\app.exe"; "--flag" ]

    [<Fact>]
    let ``split sur une chaine vide retourne une liste vide`` () =
        split "" |> should be Empty

    [<Fact>]
    let ``split sur une chaine de guillemets vides retourne une liste vide`` () =
        split "\"\"" |> should be Empty
