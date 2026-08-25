namespace Diplo.Volume.Tests

module IsoDriverTests =

    open System
    open System.IO
    open System.Text
    open Xunit
    open FsUnit.Xunit
    open Diplo.Volume.Drivers
    open Diplo.Abstractions.SecurityValidation

    do addAllowedVolumeDir (Path.GetTempPath())

    // Les images de test sont construites à la main, octet par octet, en
    // suivant la norme ECMA-167 3e édition (et son implémentation de référence
    // dans le noyau Linux, fs/udf/ecma_167.h). Aucun outil de création d'images
    // (xorriso, mkisofs/genisoimage) n'étant disponible sur la machine de test
    // — seul 7z.exe est présent — il n'est pas possible de produire ces images
    // avec un graveur conforme. La conformité des images peut être vérifiée
    // avec 7-Zip : `7z i image.iso` (détection du format UDF) puis
    // `7z l image.iso` (liste du contenu).

    let createTempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-vol-test-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    let cleanupDir (dir: string) =
        try
            if Directory.Exists(dir) then
                Directory.Delete(dir, true)
        with _ ->
            ()

    let writeBothEndian (iso: byte[]) offset (value: int) =
        let v = uint32 value
        iso.[offset] <- byte (v &&& 0xFFu)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 2] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 3] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 4] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 5] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 6] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 7] <- byte (v &&& 0xFFu)

    let writeUInt16BothEndian (iso: byte[]) offset (value: int) =
        let v = uint16 value
        iso.[offset] <- byte (v &&& 0xFFus)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 2] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 3] <- byte (v &&& 0xFFus)

    let writeRecord (iso: byte[]) offset extent dataLength flags (name: byte[]) =
        let recLen = 33 + name.Length
        let padded = if recLen % 2 = 1 then recLen + 1 else recLen
        iso.[offset] <- byte padded
        iso.[offset + 1] <- 0uy
        writeBothEndian iso (offset + 2) extent
        writeBothEndian iso (offset + 10) dataLength
        iso.[offset + 25] <- flags
        writeUInt16BothEndian iso (offset + 28) 1
        iso.[offset + 32] <- byte name.Length
        Array.Copy(name, 0, iso, offset + 33, name.Length)
        padded

    let buildIso () =
        let iso = Array.zeroCreate<byte> (22 * 2048)
        let pvd = 16 * 2048
        iso.[pvd] <- 1uy
        Array.Copy(Encoding.ASCII.GetBytes("CD001"), 0, iso, pvd + 1, 5)
        iso.[pvd + 6] <- 1uy
        writeBothEndian iso (pvd + 80) 22
        writeUInt16BothEndian iso (pvd + 128) 2048
        writeRecord iso (pvd + 156) 20 2048 0x02uy [| 0x00uy |] |> ignore
        let rootOffset = 20 * 2048
        let dot = writeRecord iso rootOffset 20 2048 0x02uy [| 0x00uy |]
        let dotdot = writeRecord iso (rootOffset + dot) 20 2048 0x02uy [| 0x01uy |]
        let content = Encoding.UTF8.GetBytes("Bonjour ISO!\n")
        let name = Encoding.ASCII.GetBytes("HELLO.TXT;1")

        writeRecord iso (rootOffset + dot + dotdot) 21 content.Length 0x00uy name
        |> ignore

        Array.Copy(content, 0, iso, 21 * 2048, content.Length)
        iso

    let writeUInt16LE (iso: byte[]) offset (value: int) =
        let v = uint16 value
        iso.[offset] <- byte (v &&& 0xFFus)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFus)

    let writeUInt32LE (iso: byte[]) offset (value: int) =
        let v = uint32 value
        iso.[offset] <- byte (v &&& 0xFFu)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 2] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 3] <- byte ((v >>> 24) &&& 0xFFu)

    // Écrit un en-tête de descripteur UDF (3/7.2) : identifiant de balise,
    // version 2 et emplacement de la balise. Le champ réservé (octet 5) et le
    // numéro de série (octets 6/7) sont mis à zéro ; le CRC (octets 8/9), la
    // longueur du CRC (octets 10/11) et la somme de contrôle (octet 4) sont
    // remplis par finalizeTag.
    let writeTag (iso: byte[]) offset tagIdent location =
        writeUInt16LE iso offset tagIdent
        writeUInt16LE iso (offset + 2) 2
        iso.[offset + 5] <- 0uy
        writeUInt16LE iso (offset + 6) 0
        writeUInt32LE iso (offset + 12) location

    // CRC-16/CCITT (polynôme 0x1021, valeur initiale 0) du champ
    // descriptorCRC de la balise UDF (3/7.2).
    let crc16 (iso: byte[]) offset count =
        let mutable crc = 0us

        for i in 0 .. count - 1 do
            crc <- crc ^^^ (uint16 iso.[offset + i] <<< 8)

            for _ in 0..7 do
                crc <-
                    if (crc &&& 0x8000us) <> 0us then
                        (crc <<< 1) ^^^ 0x1021us
                    else
                        crc <<< 1

        crc

    // Finalise une balise UDF : longueur du CRC (octets 10/11), CRC du
    // descripteur (octets 8/9) sur crcLen octets à partir de l'octet 16, puis
    // somme de contrôle (octet 4) = somme modulo 256 des octets 0..15 sauf
    // l'octet 4 (3/7.2).
    let finalizeTag (iso: byte[]) offset crcLen =
        writeUInt16LE iso (offset + 10) crcLen
        let crc = crc16 iso (offset + 16) crcLen
        writeUInt16LE iso (offset + 8) (int crc)
        let mutable sum = 0uy

        for i in 0..15 do
            if i <> 4 then
                sum <- sum + iso.[offset + i]

        iso.[offset + 4] <- sum

    // Écrit un identifiant de fichier (FID, 4/14.4) : balise 0x0101, version 1,
    // caractéristiques, longueur du nom et ICB. Le nom est un d-string dont le
    // premier octet (0x08) indique un codage ASCII. Retourne la longueur
    // totale du FID, alignée sur 4 octets.
    let writeFid (iso: byte[]) offset blockLocation characteristics (name: byte[]) icbLen icbLoc =
        writeTag iso offset 0x0101 blockLocation
        writeUInt16LE iso (offset + 16) 1
        iso.[offset + 18] <- characteristics
        iso.[offset + 19] <- byte name.Length
        writeUInt32LE iso (offset + 20) icbLen
        writeUInt32LE iso (offset + 24) icbLoc
        writeUInt16LE iso (offset + 28) 0
        writeUInt16LE iso (offset + 36) 0
        Array.Copy(name, 0, iso, offset + 38, name.Length)
        let total = 38 + name.Length
        let padded = total + ((4 - (total % 4)) % 4)
        finalizeTag iso offset (padded - 16)
        padded

    // Construit une image UDF DVD à un seul fichier, lisible par 7-Zip
    // (UdfIn.cpp) et conforme à l'ECMA-167. Structure :
    //   bloc  16 : séquence de reconnaissance de volume (VRS), descripteur NSR ;
    //   bloc 256 : pointeur de volume d'ancrage (AVDP) finalisé, VDS et miroir
    //              de longueur 6144 (trois blocs : PD + LVD + TD) ;
    //   bloc 257 : descripteur de partition (PD) de 300 blocs ;
    //   bloc 258 : descripteur de volume logique (LVD), jeu de fichiers au
    //              bloc 260, carte de partition type 1 au bloc 440 ;
    //   bloc 259 : descripteur de terminaison (TD) ;
    //   bloc 260 : descripteur de jeu de fichiers (FSD), ICB racine au bloc 261 ;
    //   bloc 261 : entrée de fichier (FE) du répertoire racine, dont les FIDs
    //              sont stockés dans le bloc alloué 263 (short_ad) ;
    //   bloc 262 : FE du fichier HELLO.TXT (long_ad vers le bloc 264) ;
    //   bloc 263 : descripteurs d'identifiant de fichier (FID) de la racine ;
    //   bloc 264 : contenu du fichier.
    // Toutes les balises sont finalisées (CRC + somme de contrôle) comme exigé
    // par la lecture 7-Zip. Les FIDs sont des d-strings ASCII (3/7.2.2). Les
    // répertoires sont identifiés par le type de fichier 4 (4/14.6) et les FIDs
    // par la balise 0x0101 (4/14.4). L'identifiant NSR est paramétrable pour
    // couvrir UDF 1.x (NSR02) et UDF 2.x (NSR03).
    let buildUdfSingleFile (nsrId: string) =
        let iso = Array.zeroCreate<byte> (267 * 2048)
        // Séquence de reconnaissance de volume (ECMA-167 3/8.4.1) : descripteur
        // BEA01 au bloc 16, descripteur NSR au bloc 17 et descripteur TEA01 au
        // bloc 18. L'identifiant NSR est paramétrable (NSR02 pour UDF 1.x,
        // NSR03 pour UDF 2.x).
        let vrs = 16 * 2048
        iso.[vrs] <- 0uy
        Array.Copy(Encoding.ASCII.GetBytes("BEA01"), 0, iso, vrs + 1, 5)
        iso.[vrs + 6] <- 1uy
        let nsr = vrs + 2048
        iso.[nsr] <- 0uy
        Array.Copy(Encoding.ASCII.GetBytes(nsrId), 0, iso, nsr + 1, 5)
        iso.[nsr + 6] <- 1uy
        writeUInt32LE iso (nsr + 12) 257
        let tea = nsr + 2048
        iso.[tea] <- 4uy
        Array.Copy(Encoding.ASCII.GetBytes("TEA01"), 0, iso, tea + 1, 5)
        iso.[tea + 6] <- 1uy
        let avdp = 256 * 2048
        writeTag iso avdp 0x0002 256
        writeUInt32LE iso (avdp + 16) 6144
        writeUInt32LE iso (avdp + 20) 257
        writeUInt32LE iso (avdp + 24) 6144
        writeUInt32LE iso (avdp + 28) 257
        finalizeTag iso avdp 16
        let vds = 257 * 2048
        writeTag iso vds 0x0005 257
        writeUInt16LE iso (vds + 22) 0
        writeUInt32LE iso (vds + 184) 4
        writeUInt32LE iso (vds + 188) 0
        writeUInt32LE iso (vds + 192) 300
        finalizeTag iso vds 180
        let lvd = vds + 2048
        writeTag iso lvd 0x0006 257
        writeUInt32LE iso (lvd + 212) 2048
        writeUInt32LE iso (lvd + 248) 2048
        writeUInt32LE iso (lvd + 252) 260
        writeUInt16LE iso (lvd + 256) 0
        writeUInt32LE iso (lvd + 264) 8
        writeUInt32LE iso (lvd + 268) 1
        iso.[lvd + 440] <- 1uy
        iso.[lvd + 441] <- 6uy
        writeUInt16LE iso (lvd + 442) 1
        writeUInt16LE iso (lvd + 444) 0
        finalizeTag iso lvd 430
        let td = lvd + 2048
        writeTag iso td 0x0008 257
        finalizeTag iso td 0
        let fsd = 260 * 2048
        writeTag iso fsd 0x0100 260
        writeUInt32LE iso (fsd + 400) 2048
        writeUInt32LE iso (fsd + 404) 261
        writeUInt16LE iso (fsd + 408) 0
        finalizeTag iso fsd 396
        let rootIcb = 261 * 2048
        writeTag iso rootIcb 0x0105 261
        iso.[rootIcb + 27] <- 4uy
        writeUInt16LE iso (rootIcb + 34) 0
        writeUInt32LE iso (rootIcb + 56) 88
        writeUInt32LE iso (rootIcb + 172) 8
        writeUInt32LE iso (rootIcb + 176) 88
        writeUInt32LE iso (rootIcb + 180) 263
        finalizeTag iso rootIcb 180
        let fileIcb = 262 * 2048
        writeTag iso fileIcb 0x0105 262
        iso.[fileIcb + 27] <- 5uy
        writeUInt16LE iso (fileIcb + 34) 1
        let content = Encoding.UTF8.GetBytes("Bonjour DVD!\n")
        writeUInt32LE iso (fileIcb + 56) content.Length
        writeUInt32LE iso (fileIcb + 172) 16
        writeUInt32LE iso (fileIcb + 176) content.Length
        writeUInt32LE iso (fileIcb + 180) 265
        writeUInt16LE iso (fileIcb + 184) 0
        finalizeTag iso fileIcb 180
        let fidBlock = 263 * 2048
        let helloName = Array.append [| 0x08uy |] (Encoding.ASCII.GetBytes("HELLO.TXT"))
        let fidLen1 = writeFid iso fidBlock 263 0uy helloName 2048 262
        let parentName = [| 0x08uy; 0x01uy |]
        let fidLen2 = writeFid iso (fidBlock + fidLen1) 263 0x08uy parentName 2048 261

        if fidLen1 + fidLen2 <> 88 then
            failwith "FIDs racine : taille inattendue"
        // Ancre de fin de volume (ECMA-167 3/8.4.2) : l'AVDP du bloc 266, après
        // le dernier contenu (bloc 265), permet à 7-Zip de trouver la fin
        // d'archive (NoEndAnchor=false).
        let endAnchor = 266 * 2048
        writeTag iso endAnchor 0x0002 266
        writeUInt32LE iso (endAnchor + 16) 6144
        writeUInt32LE iso (endAnchor + 20) 257
        writeUInt32LE iso (endAnchor + 24) 6144
        writeUInt32LE iso (endAnchor + 28) 257
        finalizeTag iso endAnchor 16
        Array.Copy(content, 0, iso, 265 * 2048, content.Length)
        iso

    let buildUdfDvd () = buildUdfSingleFile "NSR03"

    let buildUdfNsr02 () = buildUdfSingleFile "NSR02"

    // Construit une image UDF plus riche, lisible par 7-Zip, couvrant :
    //   - un fichier multi-blocs (BIG.BIN) assemblé via trois short_ad ;
    //   - des répertoires imbriqués (DOSSIER/SOUS.TXT) ;
    //   - un ICB de répertoire multi-blocs (FIDs répartis sur deux blocs
    //     alloués, 265 et 266, pointés par deux short_ad) ;
    //   - des long_ad (ICB du répertoire DOSSIER et du fichier SOUS.TXT).
    // Blocks : 256 AVDP, 257 PD, 258 LVD, 259 TD, 260 FSD, 261 ICB racine,
    // 262 ICB BIG.BIN, 263 ICB DOSSIER, 264 ICB SOUS.TXT, 265 FIDs racine,
    // 266 FID « .. » racine, 267 FIDs DOSSIER, 270-272 contenus BIG.BIN,
    // 273 contenu SOUS.TXT.
    let buildUdfMultiBlock () =
        let iso = Array.zeroCreate<byte> (275 * 2048)
        // Séquence de reconnaissance de volume (ECMA-167 3/8.4.1) : BEA01 au
        // bloc 16, NSR03 au bloc 17, TEA01 au bloc 18.
        let vrs = 16 * 2048
        iso.[vrs] <- 0uy
        Array.Copy(Encoding.ASCII.GetBytes("BEA01"), 0, iso, vrs + 1, 5)
        iso.[vrs + 6] <- 1uy
        let nsr = vrs + 2048
        iso.[nsr] <- 0uy
        Array.Copy(Encoding.ASCII.GetBytes("NSR03"), 0, iso, nsr + 1, 5)
        iso.[nsr + 6] <- 1uy
        writeUInt32LE iso (nsr + 12) 257
        let tea = nsr + 2048
        iso.[tea] <- 4uy
        Array.Copy(Encoding.ASCII.GetBytes("TEA01"), 0, iso, tea + 1, 5)
        iso.[tea + 6] <- 1uy
        let avdp = 256 * 2048
        writeTag iso avdp 0x0002 256
        writeUInt32LE iso (avdp + 16) 6144
        writeUInt32LE iso (avdp + 20) 257
        writeUInt32LE iso (avdp + 24) 6144
        writeUInt32LE iso (avdp + 28) 257
        finalizeTag iso avdp 16
        let vds = 257 * 2048
        writeTag iso vds 0x0005 257
        writeUInt16LE iso (vds + 22) 0
        writeUInt32LE iso (vds + 184) 4
        writeUInt32LE iso (vds + 188) 0
        writeUInt32LE iso (vds + 192) 300
        finalizeTag iso vds 180
        let lvd = vds + 2048
        writeTag iso lvd 0x0006 257
        writeUInt32LE iso (lvd + 212) 2048
        writeUInt32LE iso (lvd + 248) 2048
        writeUInt32LE iso (lvd + 252) 260
        writeUInt16LE iso (lvd + 256) 0
        writeUInt32LE iso (lvd + 264) 8
        writeUInt32LE iso (lvd + 268) 1
        iso.[lvd + 440] <- 1uy
        iso.[lvd + 441] <- 6uy
        writeUInt16LE iso (lvd + 442) 1
        writeUInt16LE iso (lvd + 444) 0
        finalizeTag iso lvd 430
        let td = lvd + 2048
        writeTag iso td 0x0008 257
        finalizeTag iso td 0
        let fsd = 260 * 2048
        writeTag iso fsd 0x0100 260
        writeUInt32LE iso (fsd + 400) 2048
        writeUInt32LE iso (fsd + 404) 261
        writeUInt16LE iso (fsd + 408) 0
        finalizeTag iso fsd 396
        // ICB racine : FIDs sur deux blocs alloués (265 et 266), total 136.
        let rootIcb = 261 * 2048
        writeTag iso rootIcb 0x0105 261
        iso.[rootIcb + 27] <- 4uy
        writeUInt16LE iso (rootIcb + 34) 0
        writeUInt32LE iso (rootIcb + 56) 136
        writeUInt32LE iso (rootIcb + 172) 16
        writeUInt32LE iso (rootIcb + 176) 96
        writeUInt32LE iso (rootIcb + 180) 265
        writeUInt32LE iso (rootIcb + 184) 40
        writeUInt32LE iso (rootIcb + 188) 266
        finalizeTag iso rootIcb 180
        // ICB de BIG.BIN : fichier multi-blocs (trois short_ad).
        let bigIcb = 262 * 2048
        writeTag iso bigIcb 0x0105 262
        iso.[bigIcb + 27] <- 5uy
        writeUInt16LE iso (bigIcb + 34) 0
        writeUInt32LE iso (bigIcb + 56) 15
        writeUInt32LE iso (bigIcb + 172) 24
        writeUInt32LE iso (bigIcb + 176) 5
        writeUInt32LE iso (bigIcb + 180) 270
        writeUInt32LE iso (bigIcb + 184) 5
        writeUInt32LE iso (bigIcb + 188) 271
        writeUInt32LE iso (bigIcb + 192) 5
        writeUInt32LE iso (bigIcb + 196) 272
        finalizeTag iso bigIcb 180
        // ICB de DOSSIER : long_ad vers le bloc 267 (FIDs).
        let dirIcb = 263 * 2048
        writeTag iso dirIcb 0x0105 263
        iso.[dirIcb + 27] <- 4uy
        writeUInt16LE iso (dirIcb + 34) 1
        writeUInt32LE iso (dirIcb + 56) 88
        writeUInt32LE iso (dirIcb + 172) 16
        writeUInt32LE iso (dirIcb + 176) 88
        writeUInt32LE iso (dirIcb + 180) 267
        writeUInt16LE iso (dirIcb + 184) 0
        finalizeTag iso dirIcb 180
        // ICB de SOUS.TXT : long_ad vers le bloc 273.
        let sousIcb = 264 * 2048
        writeTag iso sousIcb 0x0105 264
        iso.[sousIcb + 27] <- 5uy
        writeUInt16LE iso (sousIcb + 34) 1
        let sousContent = Encoding.UTF8.GetBytes("Bonjour SOUS\n")
        writeUInt32LE iso (sousIcb + 56) sousContent.Length
        writeUInt32LE iso (sousIcb + 172) 16
        writeUInt32LE iso (sousIcb + 176) sousContent.Length
        writeUInt32LE iso (sousIcb + 180) 273
        writeUInt16LE iso (sousIcb + 184) 0
        finalizeTag iso sousIcb 180
        // Bloc 265 : FIDs « BIG.BIN » et « DOSSIER ».
        let rootFid1 = 265 * 2048
        let bigName = Array.append [| 0x08uy |] (Encoding.ASCII.GetBytes("BIG.BIN"))
        let fidLen1 = writeFid iso rootFid1 265 0uy bigName 2048 262
        let dossierName = Array.append [| 0x08uy |] (Encoding.ASCII.GetBytes("DOSSIER"))
        let fidLen2 = writeFid iso (rootFid1 + fidLen1) 265 0x02uy dossierName 2048 263

        if fidLen1 + fidLen2 <> 96 then
            failwith "FIDs racine : taille inattendue"
        // Bloc 266 : FID « .. » de la racine.
        let rootFid2 = 266 * 2048
        let parentName = [| 0x08uy; 0x01uy |]
        writeFid iso rootFid2 266 0x08uy parentName 2048 261 |> ignore
        // Bloc 267 : FIDs de DOSSIER (« SOUS.TXT » et « .. »).
        let dirFid = 267 * 2048
        let sousName = Array.append [| 0x08uy |] (Encoding.ASCII.GetBytes("SOUS.TXT"))
        let fidLen3 = writeFid iso dirFid 267 0uy sousName 2048 264
        let fidLen4 = writeFid iso (dirFid + fidLen3) 267 0x08uy parentName 2048 263

        if fidLen3 + fidLen4 <> 88 then
            failwith "FIDs de DOSSIER : taille inattendue"
        // Ancre de fin de volume (ECMA-167 3/8.4.2) : l'AVDP du bloc 274, après
        // le dernier contenu (bloc 273), matérialise la fin d'archive pour 7-Zip
        // (NoEndAnchor=false).
        let endAnchor = 274 * 2048
        writeTag iso endAnchor 0x0002 274
        writeUInt32LE iso (endAnchor + 16) 6144
        writeUInt32LE iso (endAnchor + 20) 257
        writeUInt32LE iso (endAnchor + 24) 6144
        writeUInt32LE iso (endAnchor + 28) 257
        finalizeTag iso endAnchor 16
        Array.Copy(Encoding.ASCII.GetBytes("BIGA\n"), 0, iso, 270 * 2048, 5)
        Array.Copy(Encoding.ASCII.GetBytes("BIGB\n"), 0, iso, 271 * 2048, 5)
        Array.Copy(Encoding.ASCII.GetBytes("BIGC\n"), 0, iso, 272 * 2048, 5)
        Array.Copy(sousContent, 0, iso, 273 * 2048, sousContent.Length)
        iso

    // Construit une image UDF dont le fichier HELLO.TXT est décrit par un
    // extended_ad (4/14.13). Ce type de descripteur d'allocation est lu par
    // Diplo (décalage 20, emplacement à l'octet 12 du descripteur) mais rejeté
    // par 7-Zip ; il n'est donc validé que par un test unitaire.
    let buildUdfExtendedAd () =
        let iso = buildUdfSingleFile "NSR03"
        let fileIcb = 262 * 2048
        writeUInt16LE iso (fileIcb + 34) 2
        writeUInt32LE iso (fileIcb + 172) 20
        writeUInt32LE iso (fileIcb + 176) 13
        writeUInt32LE iso (fileIcb + 180) 0
        writeUInt32LE iso (fileIcb + 184) 0
        writeUInt32LE iso (fileIcb + 188) 265
        writeUInt16LE iso (fileIcb + 192) 0
        finalizeTag iso fileIcb 180
        iso

    let mountImage (driver: IsoDriver) (isoFile: string) =
        let (id, _) =
            driver.CreateVolume("mount-udf", Map.ofList [ "iso", isoFile ], Map.empty)

        let target =
            Path.Combine(Path.GetTempPath(), "diplo-vol-mnt-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(target) |> ignore
        let (success, mountDir) = driver.MountVolume(id, target, "")
        (success, mountDir, id)

    [<Fact>]
    let ``CreateVolume cree un volume ISO et retourne id et chemin`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, mountpoint) =
                driver.CreateVolume("test-iso", Map.ofList [ "iso", isoFile ], Map.empty)

            String.IsNullOrEmpty(id) |> should equal false
            mountpoint |> should equal isoFile
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CreateVolume sans option iso leve une exception`` () =
        let tempRoot = createTempDir ()

        try
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            (fun () -> driver.CreateVolume("bad", Map.empty, Map.empty) |> ignore)
            |> should throw typeof<System.Exception>
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``CreateVolume avec fichier ISO inexistant leve une exception`` () =
        let tempRoot = createTempDir ()

        try
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            (fun () ->
                driver.CreateVolume("bad", Map.ofList [ "iso", "Z:\\inexistant.iso" ], Map.empty)
                |> ignore)
            |> should throw typeof<System.Exception>
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait les fichiers de l ISO`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, _) =
                driver.CreateVolume("mount-iso", Map.ofList [ "iso", isoFile ], Map.empty)

            let target = Path.Combine(tempRoot, "mnt")
            Directory.CreateDirectory(target) |> ignore
            let (success, mountDir) = driver.MountVolume(id, target, "")
            success |> should equal true
            mountDir |> should equal target

            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT"))
            |> should equal "Bonjour ISO!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``UnmountVolume supprime le repertoire de montage`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, _) =
                driver.CreateVolume("unmount-iso", Map.ofList [ "iso", isoFile ], Map.empty)

            let target = Path.Combine(tempRoot, "mnt")
            Directory.CreateDirectory(target) |> ignore
            driver.MountVolume(id, target, "") |> ignore
            Directory.Exists(target) |> should equal true
            let (success, msg) = driver.UnmountVolume(id, target)
            success |> should equal true
            msg |> should equal "Démonté"
            Directory.Exists(target) |> should equal false
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``InspectVolume retourne Some pour un volume ISO existant`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, _) =
                driver.CreateVolume("inspect-iso", Map.ofList [ "iso", isoFile ], Map.empty)

            let result = driver.InspectVolume(id)
            result.IsSome |> should equal true
            result.Value.GetProperty("name").GetString() |> should equal "inspect-iso"
            result.Value.GetProperty("driver").GetString() |> should equal "iso"
            result.Value.GetProperty("remotePath").GetString() |> should equal isoFile
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume supprime le volume et retourne true`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, _) =
                driver.CreateVolume("delete-iso", Map.ofList [ "iso", isoFile ], Map.empty)

            driver.RemoveVolume(id, false) |> should equal true
            driver.InspectVolume(id).IsNone |> should equal true
            File.Exists(isoFile) |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``RemoveVolume avec force supprime aussi le fichier ISO`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))

            let (id, _) =
                driver.CreateVolume("force-delete", Map.ofList [ "iso", isoFile ], Map.empty)

            driver.RemoveVolume(id, true) |> should equal true
            driver.InspectVolume(id).IsNone |> should equal true
            File.Exists(isoFile) |> should equal false
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait les fichiers d une image DVD UDF conforme`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfDvd ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true

            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT"))
            |> should equal "Bonjour DVD!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait les fichiers d une image UDF 1.0 (NSR02)`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfNsr02 ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true

            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT"))
            |> should equal "Bonjour DVD!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume assemble un fichier multi-blocs (trois short_ad)`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfMultiBlock ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true

            File.ReadAllText(Path.Combine(mountDir, "BIG.BIN"))
            |> should equal "BIGA\nBIGB\nBIGC\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait des repertoires imbriques avec long_ad`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfMultiBlock ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true

            File.ReadAllText(Path.Combine(mountDir, "DOSSIER", "SOUS.TXT"))
            |> should equal "Bonjour SOUS\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume extrait un fichier decrit par un extended_ad`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfExtendedAd ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true

            File.ReadAllText(Path.Combine(mountDir, "HELLO.TXT"))
            |> should equal "Bonjour DVD!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``MountVolume parcourt un ICB de repertoire multi-blocs`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfMultiBlock ())
            let driver = IsoDriver(Path.Combine(tempRoot, "data"))
            let (success, mountDir, _) = mountImage driver isoFile
            success |> should equal true
            Directory.Exists(Path.Combine(mountDir, "DOSSIER")) |> should equal true
            File.Exists(Path.Combine(mountDir, "BIG.BIN")) |> should equal true
            File.Exists(Path.Combine(mountDir, "DOSSIER", "SOUS.TXT")) |> should equal true
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``IsoImage.readFile lit un fichier ISO9660 par son chemin`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())

            Encoding.UTF8.GetString(IsoImage.readFile isoFile "/HELLO.TXT")
            |> should equal "Bonjour ISO!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``IsoImage.readFile lit un fichier UDF par son chemin`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfDvd ())

            Encoding.UTF8.GetString(IsoImage.readFile isoFile "/HELLO.TXT")
            |> should equal "Bonjour DVD!\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``IsoImage.readFile lit un fichier UDF dans un sous-repertoire`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildUdfMultiBlock ())

            Encoding.UTF8.GetString(IsoImage.readFile isoFile "/DOSSIER/SOUS.TXT")
            |> should equal "Bonjour SOUS\n"
        finally
            cleanupDir tempRoot

    [<Fact>]
    let ``IsoImage.readFile leve une exception si le fichier est absent`` () =
        let tempRoot = createTempDir ()

        try
            let isoFile = Path.Combine(tempRoot, "test.iso")
            File.WriteAllBytes(isoFile, buildIso ())

            (fun () -> IsoImage.readFile isoFile "/INEXISTANT.TXT" |> ignore)
            |> should throw typeof<System.Exception>
        finally
            cleanupDir tempRoot
