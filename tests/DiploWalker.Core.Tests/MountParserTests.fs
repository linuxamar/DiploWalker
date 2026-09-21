module DiploWalker.Core.Tests.MountParserTests

open Xunit
open FsUnit.Xunit
open DiploWalker.Core.Mounts

[<Fact>]
let ``parse analyse un montage en lecture-ecriture`` () =
    MountParser.parse @"src=C:\donnees,dst=C:\conteneur\donnees"
    |> should equal [ (@"C:\donnees", @"C:\conteneur\donnees", false) ]

[<Fact>]
let ``parse analyse un montage en lecture seule`` () =
    MountParser.parse @"src=C:\donnees,dst=C:\conteneur\donnees,ro"
    |> should equal [ (@"C:\donnees", @"C:\conteneur\donnees", true) ]

[<Fact>]
let ``parse ignore les lignes incompletes`` () =
    let text =
        String.concat "\n" [ @"src=C:\donnees,dst=C:\conteneur\donnees"; @"src=C:\sans-destination" ]

    MountParser.parse text
    |> should equal [ (@"C:\donnees", @"C:\conteneur\donnees", false) ]

[<Fact>]
let ``parse accepte plusieurs montages separes par un point-virgule`` () =
    MountParser.parse @"src=C:\a,dst=D:\a;src=C:\b,dst=D:\b,ro"
    |> should equal [ (@"C:\a", @"D:\a", false); (@"C:\b", @"D:\b", true) ]

[<Fact>]
let ``parse accepte plusieurs montages separes par des retours a la ligne`` () =
    let text = String.concat "\n" [ @"src=C:\a,dst=D:\a"; @"src=C:\b,dst=D:\b,ro" ]

    MountParser.parse text
    |> should equal [ (@"C:\a", @"D:\a", false); (@"C:\b", @"D:\b", true) ]

[<Fact>]
let ``parse renvoie une liste vide sans texte`` () = MountParser.parse "" |> should be Empty

[<Fact>]
let ``parseArray analyse un element par entree`` () =
    MountParser.parseArray [| @"src=C:\a,dst=D:\a"; @"src=C:\b,dst=D:\b,ro" |]
    |> should equal [ (@"C:\a", @"D:\a", false); (@"C:\b", @"D:\b", true) ]

[<Fact>]
let ``parseArray renvoie une liste vide sans entree`` () =
    MountParser.parseArray [||] |> should be Empty

[<Fact>]
let ``parse est insensible a la casse pour src dst et ro`` () =
    MountParser.parse @"SRC=C:\a,DST=D:\a,RO"
    |> should equal [ (@"C:\a", @"D:\a", true) ]

