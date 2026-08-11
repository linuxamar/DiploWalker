namespace Diplo.Disk

open System.IO
open System.Text

/// Détection du format d'une image disque à partir des signatures binaires
/// (magic bytes) en tête et en pied de fichier.
module DiskFormat =

    /// Formats d'images disque reconnus par Diplo.
    type Format =
        | Qcow2
        | Qcow1
        | Vhd
        | Vhdx
        | Vmdk
        | Raw
        | Unknown

    let toString (format: Format) =
        match format with
        | Qcow2 -> "qcow2"
        | Qcow1 -> "qcow (v1)"
        | Vhd -> "vhd"
        | Vhdx -> "vhdx"
        | Vmdk -> "vmdk"
        | Raw -> "raw"
        | Unknown -> "inconnu"

    let private startsWith (data: byte[]) (offset: int) (pattern: byte[]) =
        data.Length - offset >= pattern.Length
        && Array.forall2 (fun a b -> a = b) data.[offset .. offset + pattern.Length - 1] pattern

    /// Détecte le format d'un fichier image disque.
    /// La signature VHD (« conectix ») se trouve dans le footer de 512 octets
    /// en fin de fichier (VHD fixe et dynamique). Les autres signatures sont
    /// en début de fichier.
    let detect (path: string) : Format =
        use fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
        if fs.Length < 4L then
            Raw
        else
            let head = Array.zeroCreate<byte> 16
            fs.Position <- 0L
            let headRead = fs.Read(head, 0, 16)
            if headRead < 4 then
                Raw
            else
                let isQcow2 = startsWith head 0 [| byte 'Q'; byte 'F'; byte 'I'; 0xFBuy |]
                let isQcow1 = startsWith head 0 [| byte 'Q'; byte 'F'; byte 'I'; 0xFEuy |]
                let isVhdx = headRead >= 8 && Encoding.ASCII.GetString(head, 0, 8) = "vhdxfile"
                let isVmdk = startsWith head 0 [| byte 'K'; byte 'D'; byte 'M'; byte 'V' |]
                if isQcow2 then Qcow2
                elif isQcow1 then Qcow1
                elif isVhdx then Vhdx
                elif isVmdk then Vmdk
                else
                    let foot = Array.zeroCreate<byte> 512
                    if fs.Length < 512L then
                        Raw
                    else
                        fs.Position <- fs.Length - 512L
                        fs.Read(foot, 0, 512) |> ignore
                        let isVhd = startsWith foot 0 (Encoding.ASCII.GetBytes "conectix")
                        if isVhd then Vhd else Raw

    /// Indique si le format est une image disque prise en charge par le
    /// moteur de montage (c'est-à-dire un fichier, pas un répertoire).
    let isDiskImage (format: Format) =
        match format with
        | Qcow2 | Vhd | Vhdx | Vmdk | Raw -> true
        | Qcow1 | Unknown -> false
