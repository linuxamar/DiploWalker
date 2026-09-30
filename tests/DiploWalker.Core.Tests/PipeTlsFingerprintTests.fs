namespace DiploWalker.Core.Tests

/// Garde-fou de l'ancrage TLS.
///
/// `PipeTls.ExpectedServerThumbprint` est une constante compilée : si la PKI est
/// régénérée, le client continuerait d'épingler l'ancien certificat et tous les
/// appels `https://pipe:/…` échoueraient — sur une machine de développement
/// comme en production. Ce test compare donc la constante au certificat public
/// versionné en clair (non chiffré par git-crypt, donc toujours lisible).
module PipeTlsFingerprintTests =

    open System
    open System.IO
    open System.Security.Cryptography.X509Certificates
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions

    /// Remonte depuis le dossier de sortie du test jusqu'à la racine du dépôt.
    let private findRepositoryRoot () =
        let marker = Path.Combine("certificates", "leaf-tls-server", "certs", "leaf-tls-server.crt.pem")

        let rec walk (dir: DirectoryInfo) =
            if isNull (box dir) then
                failwithf "Racine du dépôt introuvable en remontant depuis le dossier de test (marqueur attendu : %s)." marker

            if File.Exists(Path.Combine(dir.FullName, marker)) then
                dir.FullName
            else
                walk dir.Parent

        walk (DirectoryInfo(AppContext.BaseDirectory))

    let private serverCertificatePath =
        Path.Combine(findRepositoryRoot (), "certificates", "leaf-tls-server", "certs", "leaf-tls-server.crt.pem")

    [<Fact>]
    /// L'empreinte compilée doit correspondre au certificat serveur versionné.
    let ``L'empreinte ancree correspond au certificat serveur de la PKI`` () =
        File.Exists serverCertificatePath |> should equal true

        use certificate = X509CertificateLoader.LoadCertificateFromFile serverCertificatePath
        certificate.Thumbprint |> should equal PipeTls.ExpectedServerThumbprint

    [<Fact>]
    /// Détecte une constante mal saisie (guillemets, tirets, longueur fausse)
    /// avant qu'elle ne se manifeste comme un échec de connexion opaque.
    let ``L'empreinte ancree est un SHA-1 hexadecimal sur 40 caracteres`` () =
        PipeTls.ExpectedServerThumbprint |> should haveLength 40
        PipeTls.normalizeThumbprint PipeTls.ExpectedServerThumbprint
        |> should equal PipeTls.ExpectedServerThumbprint
