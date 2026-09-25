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
        split "cmd /c \"echo bonjour le monde\""
        |> should equal [ "cmd"; "/c"; "echo bonjour le monde" ]

    [<Fact>]
    let ``split retire les guillemets`` () =
        split "exec \"C:\\Program Files\\app.exe\" --flag"
        |> should equal [ "exec"; "C:\\Program Files\\app.exe"; "--flag" ]

    [<Fact>]
    let ``split sur une chaine vide retourne une liste vide`` () = split "" |> should be Empty

    [<Fact>]
    let ``split sur une chaine de guillemets vides retourne une liste vide`` () = split "\"\"" |> should be Empty

    [<Fact>]
    let ``join sur des arguments vides retourne une chaine vide`` () = join [] |> should equal ""

    [<Fact>]
    let ``join entoure de guillemets les arguments contenant des espaces`` () =
        join [ "exec"; "C:\\Program Files\\app.exe"; "--flag" ]
        |> should equal "exec \"C:\\Program Files\\app.exe\" --flag"

    [<Fact>]
    let ``join entoure de guillemets un argument vide`` () = join [ "" ] |> should equal "\"\""

    [<Fact>]
    let ``join echappe les guillemets internes en les doublant`` () =
        join [ "a\"b" ] |> should equal "\"a\"\"b\""

    [<Fact>]
    let ``split et join sont inverses l'un de l'autre`` () =
        split (join [ "exec"; "C:\\Program Files\\app.exe"; "--flag" ])
        |> should equal [ "exec"; "C:\\Program Files\\app.exe"; "--flag" ]

    [<Fact>]
    let ``split interprete les guillemets echappes en guillemet litteral`` () =
        split "\"a\"\"b\"" |> should equal [ "a\"b" ]

    [<Fact>]
    let ``split et join sont inverses avec un guillemet dans l'argument`` () =
        split (join [ "echo"; "a\"b" ]) |> should equal [ "echo"; "a\"b" ]

    [<Fact>]
    let ``split leve sur des guillemets non equilibres`` () =
        (fun () -> split "cmd \"foo" |> ignore) |> should throw typeof<System.ArgumentException>

    [<Fact>]
    let ``split ne leve pas sur des guillemets echappes equilibrants`` () =
        split "\"a\"\"b\" c" |> should equal [ "a\"b"; "c" ]
