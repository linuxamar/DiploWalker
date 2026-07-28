namespace Diplo.Core.Tests

open Xunit
open FsUnit.Xunit
open Diplo.Core.Compose

module ComposeModelsTests =

    [<Fact>]
    let ``buildContainerName correctly formats name`` () =
        buildContainerName "myproject" "web" 0
        |> should equal "myproject_web_0"

    [<Fact>]
    let ``buildContainerName with non-zero index`` () =
        buildContainerName "proj" "api" 3
        |> should equal "proj_api_3"

    [<Fact>]
    let ``buildServiceLabels contains project and service`` () =
        let labels = buildServiceLabels "proj" "web"
        labels.[composeProjectLabel] |> should equal "proj"
        labels.[composeServiceLabel] |> should equal "web"

    [<Fact>]
    let ``parsePorts handles host:container`` () =
        let ports = parsePorts ["8080:80/tcp"]
        ports |> should haveLength 1
        ports.[0].HostPort |> should equal (Some 8080)
        ports.[0].ContainerPort |> should equal 80
        ports.[0].Protocol |> should equal "tcp"

    [<Fact>]
    let ``parsePorts handles container-only`` () =
        let ports = parsePorts ["3000"]
        ports |> should haveLength 1
        ports.[0].HostPort |> should equal None
        ports.[0].ContainerPort |> should equal 3000

    [<Fact>]
    let ``parseVolumes handles source:target`` () =
        let vols = parseVolumes ["/data:/app/data"]
        vols |> should haveLength 1
        vols.[0].Source |> should equal "/data"
        vols.[0].Target |> should equal "/app/data"
        vols.[0].ReadOnly |> should equal false

    [<Fact>]
    let ``parseVolumes handles read-only`` () =
        let vols = parseVolumes ["/config:/etc/config:ro"]
        vols |> should haveLength 1
        vols.[0].ReadOnly |> should equal true

    [<Fact>]
    let ``parseEnvironment handles key=value`` () =
        let env = parseEnvironment ["FOO=bar"; "BAZ=qux"]
        env |> should haveLength 2
        env.[0].Key |> should equal "FOO"
        env.[0].Value |> should equal "bar"
        env.[1].Key |> should equal "BAZ"
        env.[1].Value |> should equal "qux"

    [<Fact>]
    let ``parseLabels handles key=value pairs`` () =
        let labels = parseLabels ["app=test"; "tier=frontend"]
        labels.["app"] |> should equal "test"
        labels.["tier"] |> should equal "frontend"

    [<Fact>]
    let ``mapPortsToEnv creates indexed env vars`` () =
        let ports = [{ ContainerPort = 80; HostPort = Some 8080; Protocol = "tcp" }]
        let env = mapPortsToEnv ports
        env |> should haveLength 1
        env.[0] |> should equal ("DIPLO_PORT_0", "8080:80")
