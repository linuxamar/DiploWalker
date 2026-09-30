module DiploWalker.Abstractions.PipeTls

open System
open System.IO
open System.Net.Security
open System.Security.Cryptography.X509Certificates

/// Nom d'hôte présenté au serveur lors de la négociation TLS.
///
/// Un named pipe n'a pas de nom d'hôte résolvable : le canal gRPC est donc
/// construit sur une adresse `https://localhost` et la tubulure branchée par
/// `ConnectCallback`. Ce nom ne sert qu'à la négociation ALPN/TLS — l'identité
/// du serveur est assurée par l'empreinte, pas par le nom (voir
/// `pinExpectedCertificate`).
[<Literal>]
let TargetHost = "localhost"

/// Empreinte SHA-1 du certificat serveur attendu sur les named pipes chiffrés.
///
/// Correspond au certificat `certificates/leaf-tls-server/` émis par
/// l'autorité « Authentification » de la PKI du dépôt (SAN localhost,
/// EKU serverAuth). L'empreinte est ancrée en dur plutôt que lue dans la
/// configuration : le certificat est un artefact du dépôt, identique sur
/// toutes les installations, et cela permet de basculer le défaut sur
/// `https://pipe:/` sans action de l'utilisateur.
///
/// Si la PKI est régénérée, cette valeur doit être mise à jour : le test
/// `PipeTlsFingerprintTests` compare la constante au certificat public
/// versionné en clair (`certs/leaf-tls-server.crt.pem`) et échoue sinon.
[<Literal>]
let ExpectedServerThumbprint = "F65FD34BB40B8A653451CD4A6325C682F6EC3FB4"

/// Retire les séparateurs d'une empreinte pour comparer deux saisies
/// (`F6:5F:D3…`, `f65fd3…` sont équivalentes).
let normalizeThumbprint (thumbprint: string) =
    if String.IsNullOrWhiteSpace(thumbprint) then
        String.Empty
    else
        thumbprint.Replace(":", "").Replace(" ", "").Trim().ToUpperInvariant()

/// Charge le certificat serveur (clé privée + chaîne) depuis un fichier PFX.
///
/// `EphemeralKeySet` garde la clé privée en mémoire sans la rendre persistante
/// dans le magasin de l'utilisateur : le service n'a pas besoin de droits
/// d'administration pour démarrer, et rien ne reste sur disque après l'arrêt.
let loadServerCertificate (path: string) : X509Certificate2 =
    if String.IsNullOrWhiteSpace(path) then
        invalidArg (nameof path) "Chemin de certificat serveur vide"

    if not (File.Exists path) then
        raise (FileNotFoundException(sprintf "Certificat serveur introuvable : %s" path, path))

    // PFX émis sans phrase de passe par la PKI de démonstration (cf.
    // certificates/README.md). Un PFX protégé par mot de passe exigerait de
    // le chemicaliser dans la configuration, ce que l'on évite ici.
    X509CertificateLoader.LoadPkcs12FromFile(path, "", X509KeyStorageFlags.EphemeralKeySet)

/// Construit un callback de validation ancrant l'empreinte du certificat.
///
/// La validation de nom est volontairement écartée : `TargetHost` vaut
/// `localhost` et ne distingue pas un service d'un autre. C'est l'empreinte
/// qui identifie le serveur. Ancrer une feuille précise suffit — elle est
/// émise par l'autorité « Authentification » de la PKI du dépôt, dont
/// l'identité est connue par construction.
let pinExpectedCertificate (expectedThumbprint: string) : RemoteCertificateValidationCallback =
    let expected = normalizeThumbprint expectedThumbprint

    if String.IsNullOrEmpty expected then
        invalidArg (nameof expectedThumbprint) "Empreinte de certificat attendue vide"

    RemoteCertificateValidationCallback(fun _ certificate _ _ ->
        match certificate with
        | null -> false
        | _ ->
            let thumbprint =
                match certificate with
                | :? X509Certificate2 as cert -> cert.Thumbprint
                | _ -> (X509CertificateLoader.LoadCertificate(certificate.GetRawCertData())).Thumbprint

            String.Equals(normalizeThumbprint thumbprint, expected, StringComparison.OrdinalIgnoreCase))

/// Callback ancrant le certificat de `certificates/leaf-tls-server/`.
let pinExpectedServerCertificate = pinExpectedCertificate ExpectedServerThumbprint
