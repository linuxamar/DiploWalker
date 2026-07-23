namespace Diplo.Network.Tests

module CniParsingTests =

    open Xunit
    open FsUnit.Xunit
    open Diplo.Network.Plugins

    let validCniJson =
        """{
            "cniVersion": "1.0.0",
            "interfaces": [
                {
                    "name": "eth0",
                    "mac": "00:11:22:33:44:55",
                    "sandbox": "/proc/123/ns/net",
                    "ips": [
                        {
                            "version": "4",
                            "address": "10.22.0.2/24",
                            "gateway": "10.22.0.1"
                        }
                    ]
                }
            ],
            "dns": {
                "nameservers": ["10.22.0.1"],
                "domain": "cluster.local",
                "options": []
            }
        }"""

    [<Fact>]
    let ``parseCniResult extrait le nom d'interface`` () =
        let ifname, _, _ = parseCniResult validCniJson
        ifname |> should equal "eth0"

    [<Fact>]
    let ``parseCniResult extrait l'adresse IPv4`` () =
        let _, ipv4, _ = parseCniResult validCniJson
        ipv4 |> should equal "10.22.0.2/24"

    [<Fact>]
    let ``parseCniResult extrait le gateway DNS`` () =
        let _, _, gw = parseCniResult validCniJson
        gw |> should equal "10.22.0.1"

    [<Fact>]
    let ``parseCniResult retourne tuple complet`` () =
        let ifname, ipv4, gw = parseCniResult validCniJson
        ifname |> should equal "eth0"
        ipv4 |> should equal "10.22.0.2/24"
        gw |> should equal "10.22.0.1"

    [<Fact>]
    let ``parseCniResult avec JSON invalide retourne chaine vide`` () =
        let ifname, ipv4, gw = parseCniResult "{invalid json"
        ifname |> should equal ""
        ipv4 |> should equal ""
        gw |> should equal ""

    [<Fact>]
    let ``parseCniResult avec JSON vide retourne chaine vide`` () =
        let ifname, ipv4, gw = parseCniResult "{}"
        ifname |> should equal ""
        ipv4 |> should equal ""
        gw |> should equal ""

    [<Fact>]
    let ``parseCniResult sans interfaces retourne chaine vide`` () =
        let json = """{"dns": {"nameservers": ["10.0.0.1"]}}"""
        let ifname, ipv4, _ = parseCniResult json
        ifname |> should equal ""
        ipv4 |> should equal ""

    [<Fact>]
    let ``parseCniResult sans DNS retourne chaine vide pour gateway`` () =
        let json =
            """{
                "interfaces": [
                    {
                        "name": "eth0",
                        "ips": [{"version": "4", "address": "10.0.0.2/24"}]
                    }
                ]
            }"""
        let ifname, ipv4, gw = parseCniResult json
        ifname |> should equal "eth0"
        ipv4 |> should equal "10.0.0.2/24"
        gw |> should equal ""

    [<Fact>]
    let ``parseCniResult avec interfaces vides retourne chaine vide`` () =
        let json = """{"interfaces": []}"""
        let ifname, ipv4, gw = parseCniResult json
        ifname |> should equal ""
        ipv4 |> should equal ""
        gw |> should equal ""

    [<Fact>]
    let ``parseCniResult avec plusieurs interfaces prend la premiere`` () =
        let json =
            """{
                "interfaces": [
                    {"name": "eth0", "ips": [{"address": "10.0.0.2/24"}]},
                    {"name": "eth1", "ips": [{"address": "192.168.1.2/24"}]}
                ],
                "dns": {"nameservers": ["8.8.8.8"]}
            }"""
        let ifname, ipv4, gw = parseCniResult json
        ifname |> should equal "eth0"
        ipv4 |> should equal "10.0.0.2/24"
        gw |> should equal "8.8.8.8"
