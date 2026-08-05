namespace Diplo.Linux.Tests

open System
open System.IO
open System.Text
open Xunit
open FsUnit.Xunit
open Diplo.Linux
open Diplo.Volume.Drivers

module BootIsoTests =

    let private writeBothEndian (iso: byte[]) offset (value: int) =
        let v = uint32 value
        iso.[offset] <- byte (v &&& 0xFFu)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 2] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 3] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 4] <- byte ((v >>> 24) &&& 0xFFu)
        iso.[offset + 5] <- byte ((v >>> 16) &&& 0xFFu)
        iso.[offset + 6] <- byte ((v >>> 8) &&& 0xFFu)
        iso.[offset + 7] <- byte (v &&& 0xFFu)

    let private writeUInt16BothEndian (iso: byte[]) offset (value: int) =
        let v = uint16 value
        iso.[offset] <- byte (v &&& 0xFFus)
        iso.[offset + 1] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 2] <- byte ((v >>> 8) &&& 0xFFus)
        iso.[offset + 3] <- byte (v &&& 0xFFus)

    let private writeRecord (iso: byte[]) offset extent dataLength flags (name: byte[]) =
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

    /// Construit une image ISO9660 minimale contenant un répertoire 'boot'
    /// qui abrite un unique fichier nommé 'name' (avec version ';1') dont le
    /// contenu est 'content', accessible via le chemin "/boot/<name>".
    let private buildBootIso (name: byte[]) (content: byte[]) =
        let iso = Array.zeroCreate<byte> (23 * 2048)
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
        let bootName = Encoding.ASCII.GetBytes("boot")
        writeRecord iso (rootOffset + dot + dotdot) 22 2048 0x02uy bootName |> ignore
        let bootOffset = 22 * 2048
        let bdot = writeRecord iso bootOffset 22 2048 0x02uy [| 0x00uy |]
        let bdotdot = writeRecord iso (bootOffset + bdot) 22 2048 0x02uy [| 0x01uy |]
        let versioned = Array.append name (Encoding.ASCII.GetBytes(";1"))
        writeRecord iso (bootOffset + bdot + bdotdot) 21 content.Length 0x00uy versioned |> ignore
        Array.Copy(content, 0, iso, 21 * 2048, content.Length)
        iso

    [<Fact>]
    let ``Le noyau charge depuis une image ISO et s'exécute`` () =
        let kernelBytes = BootImage.createKernel [ "Bonjour depuis l'ISO !" ]
        let iso = buildBootIso (Encoding.ASCII.GetBytes("diplo-kernel")) kernelBytes
        let isoFile = Path.Combine(Path.GetTempPath(), "diplo-boot-" + Guid.NewGuid().ToString("N") + ".iso")
        try
            File.WriteAllBytes(isoFile, iso)
            let kernel = IsoImage.readFile isoFile "/boot/diplo-kernel"
            kernel |> should equal kernelBytes
            use machine = new LinuxMachine(kernel, [||])
            use buffer = new MemoryStream()
            machine.StandardOutput <- buffer
            machine.Run() |> should equal 0
            let text = Encoding.UTF8.GetString(buffer.ToArray())
            text.Contains("Bonjour depuis l'ISO !") |> should be True
        finally
            if File.Exists(isoFile) then File.Delete(isoFile)

    [<Fact>]
    let ``Le chargement échoue si le noyau est absent de l'image`` () =
        let iso = buildBootIso (Encoding.ASCII.GetBytes("autre-fichier")) (Encoding.ASCII.GetBytes("pas le noyau"))
        let isoFile = Path.Combine(Path.GetTempPath(), "diplo-boot-" + Guid.NewGuid().ToString("N") + ".iso")
        try
            File.WriteAllBytes(isoFile, iso)
            (fun () -> IsoImage.readFile isoFile "/boot/diplo-kernel" |> ignore)
            |> should throw typeof<System.Exception>
        finally
            if File.Exists(isoFile) then File.Delete(isoFile)
