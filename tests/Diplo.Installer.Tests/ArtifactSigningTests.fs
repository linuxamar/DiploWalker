namespace Diplo.Installer.Tests

module ArtifactSigningTests =

    open System
    open System.Text
    open Xunit
    open FsUnit.Xunit
    open Diplo.Installer.ArtifactSigning

    // ─── Parsing ──────────────────────────────────────────────────────────

    [<Fact>]
    let ``parseManifest ignore les commentaires et les lignes vides`` () =
        let content =
            "# commentaire
sha256  a.tar.gz  ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789

sha256  b.tgz    0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"

        let map = parseManifest content
        map |> should haveCount 2

    [<Fact>]
    let ``parseManifest rejette une ligne avec un en-tête inconnu`` () =
        let content = "md5  a.tar.gz  abc"
        (fun () -> parseManifest content |> ignore) |> should throw typeof<Exception>

    [<Fact>]
    let ``parseManifest rejette un nom de fichier dupliqué`` () =
        let content =
            "sha256  a.tar.gz  0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCD
sha256  a.tar.gz  0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCD"

        (fun () -> parseManifest content |> ignore) |> should throw typeof<Exception>

    [<Fact>]
    let ``parseManifest rejette un condensat non hexadécimal de 64 caractères`` () =
        let content = "sha256  a.tar.gz  pas-un-condensat"
        (fun () -> parseManifest content |> ignore) |> should throw typeof<Exception>

    [<Fact>]
    let ``parseManifest accepte la casse des condensats en majuscules ou minuscules`` () =
        let up = "sha256  a.tar.gz  ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789"
        let low = "sha256  b.tar.gz  abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
        let map = parseManifest (up + "\n" + low)
        map |> should haveCount 2

    // ─── Vérification de signature pure ───────────────────────────────────

    let private makeKeyPair () =
        let rsa = System.Security.Cryptography.RSA.Create(4096)
        let pubPem = rsa.ExportSubjectPublicKeyInfoPem()
        let privPem = rsa.ExportPkcs8PrivateKeyPem()
        pubPem, privPem

    [<Fact>]
    let ``verifyDetachedSignature accepte une signature valide et rejette un contenu altéré`` () =
        let pubPem, privPem = makeKeyPair ()
        let data = Encoding.UTF8.GetBytes("contenu signé")

        use rsa = System.Security.Cryptography.RSA.Create()
        rsa.ImportFromPem(privPem)
        let signature =
            rsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA384, System.Security.Cryptography.RSASignaturePadding.Pkcs1)

        verifyDetachedSignature pubPem data signature |> should equal true

        let tampered = Encoding.UTF8.GetBytes("contenu altéré")
        verifyDetachedSignature pubPem tampered signature |> should equal false

    // ─── Manifeste embarqué (tests d'intégration) ────────────────────────

    [<Fact>]
    let ``loadVerifiedManifest vérifie et charge le manifeste embarqué signé`` () =
        let map = loadVerifiedManifest ()
        map |> should haveCount 4

    [<Fact>]
    let ``loadVerifiedManifest retrouve les condensats des artifacts connus`` () =
        let map = loadVerifiedManifest ()

        lookupChecksum map "containerd-1.7.27-windows-amd64.tar.gz"
        |> should equal
            (Some "2C51135531ED9EEC3D414CC40E0BF1F0203ABBE48F449BE3CF04BB47F31C7FA5")

        lookupChecksum map "containerd-1.6.36-windows-amd64.tar.gz"
        |> should equal
            (Some "74EEC7B76EBFF2A68DD478413B1ED03D435E03A4DB3244F36E92C8B80AD90C71")

        lookupChecksum map "cni-plugins-windows-amd64-1.6.2.tgz"
        |> should equal
            (Some "7D1A7FBB0C8B272801E7E64CC1CAD6939E0E7AD0F52644EE9F8801D61DAD5849")

        lookupChecksum map "windows-container-networking-cni-amd64-v0.3.1.zip"
        |> should equal (Some "4F36EE6905ADA238CA2A9E1BFB8A1FB2912C2D88C4B6E5AF4C41A42DB70D7D68")

    [<Fact>]
    let ``lookupChecksum renvoie None pour un fichier inconnu`` () =
        let map = loadVerifiedManifest ()
        lookupChecksum map "inconnu.tar.gz" |> should equal None

    [<Fact>]
    let ``verifyDetachedSignature rejette une signature produite par une autre clé`` () =
        let pubPem, privPem = makeKeyPair ()
        let otherPub, otherPriv = makeKeyPair ()
        let data = Encoding.UTF8.GetBytes("contenu")

        use rsa = System.Security.Cryptography.RSA.Create()
        rsa.ImportFromPem(privPem)
        let signature =
            rsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA384, System.Security.Cryptography.RSASignaturePadding.Pkcs1)

        verifyDetachedSignature otherPub data signature |> should equal false