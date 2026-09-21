namespace DiploWalker.Disk

open System.IO
open System.Text

/// DÃ©tection du format d'une image disque Ã  partir des signatures binaires
/// (magic bytes) en tÃªte et en pied de fichier.
module DiskFormat =

    /// Formats d'images disque reconnus par DiploWalker.
    type Format =
        | Qcow2
        | Qcow1
        | Vhd
        | Vhdx
        | Vmdk
        | Vdi
        | Dmg
        | Parallels
        | Raw
        | Iso
        | Unknown

    let toString (format: Format) =
        match format with
        | Qcow2 -> "qcow2"
        | Qcow1 -> "qcow (v1)"
        | Vhd -> "vhd"
        | Vhdx -> "vhdx"
        | Vmdk -> "vmdk"
        | Vdi -> "vdi"
        | Dmg -> "dmg"
        | Parallels -> "parallels"
        | Raw -> "raw"
        | Iso -> "iso"
        | Unknown -> "inconnu"

    let private startsWith (data: byte[]) (offset: int) (pattern: byte[]) =
        data.Length - offset >= pattern.Length
        && Array.forall2 (fun a b -> a = b) data.[offset .. offset + pattern.Length - 1] pattern

    /// DÃ©tecte le format d'un fichier image disque.
    /// La signature VHD (Â« conectix Â») se trouve dans le footer de 512 octets
    /// en fin de fichier (VHD fixe et dynamique). Les autres signatures sont
    /// en dÃ©but de fichier.
    let detect (path: string) : Format =
        use fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)

        if fs.Length < 4L then
            Raw
        else
            // 72 octets : assez pour la signature VDI situÃ©e Ã  l'offset 0x44.
            let head = Array.zeroCreate<byte> 80
            fs.Position <- 0L
            let headRead = fs.Read(head, 0, 80)

            if headRead < 4 then
                Raw
            else
                let isQcow2 = startsWith head 0 [| byte 'Q'; byte 'F'; byte 'I'; 0xFBuy |]
                let isQcow1 = startsWith head 0 [| byte 'Q'; byte 'F'; byte 'I'; 0xFEuy |]
                let isVhdx = headRead >= 8 && Encoding.ASCII.GetString(head, 0, 8) = "vhdxfile"
                let isVmdk = startsWith head 0 [| byte 'K'; byte 'D'; byte 'M'; byte 'V' |]

                if isQcow2 then
                    Qcow2
                elif isQcow1 then
                    Qcow1
                elif isVhdx then
                    Vhdx
                elif isVmdk then
                    Vmdk
                else
                    // VDI rÃ©el (VirtualBox) : signature 7F 10 DA BE Ã  l'offset 0x44.
                    let isVdi =
                        headRead >= 68
                        && startsWith head 64 [| 0x7Fuy; 0x10uy; 0xDAuy; 0xBEuy |]

                    // Parallels maison : alignÃ© sur Parallels.readHeader
                    // (readUInt32LE == 0x30617261 â†’ octets Â« ara0 Â»).
                    let isParallels =
                        startsWith head 0 [| 0x61uy; 0x72uy; 0x61uy; 0x30uy |]

                    if isVdi then
                        Vdi
                    elif isParallels then
                        Parallels
                    else
                        // DMG (UDIF) : le magic Â« koly Â» est dans le TRAILER de
                        // 512 octets en fin de fichier, pas en tÃªte.
                        let isDmg =
                            fs.Length >= 512L
                            && (let foot = Array.zeroCreate<byte> 512
                                fs.Position <- fs.Length - 512L
                                fs.Read(foot, 0, 512) |> ignore
                                startsWith foot 0 (Encoding.ASCII.GetBytes "koly"))

                        if isDmg then
                            Dmg
                        else
                            let foot = Array.zeroCreate<byte> 512

                            if fs.Length < 512L then
                                Raw
                            else
                                fs.Position <- fs.Length - 512L
                                fs.Read(foot, 0, 512) |> ignore
                                let isVhd = startsWith foot 0 (Encoding.ASCII.GetBytes "conectix")

                                if isVhd then
                                    Vhd
                                else
                                    // ISO9660 / UDF : l'identifiant Â« CD001 Â» se
                                    // trouve au bloc 16, octet 1 (offset 0x8001),
                                    // juste aprÃ¨s l'octet de type de descripteur.
                                    let isIso =
                                        fs.Length >= 32774L
                                        && (let sig' = Array.zeroCreate<byte> 6
                                            fs.Position <- 32769L
                                            fs.Read(sig', 0, 6) |> ignore
                                            Encoding.ASCII.GetString(sig', 0, 5) = "CD001")

                                    if isIso then Iso else Raw

    /// Indique si le format est une image disque prise en charge par le
    /// moteur de montage (c'est-Ã -dire un fichier, pas un rÃ©pertoire).
    let isDiskImage (format: Format) =
        match format with
        | Qcow2
        | Qcow1
        | Vhd
        | Vhdx
        | Vmdk
        | Vdi
        | Dmg
        | Parallels
        | Raw
        | Iso -> true
        | Unknown -> false


