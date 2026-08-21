namespace Diplo.Disk.Tests

module FsImageTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Disk

    let private run (f: string -> string -> unit) =
        let root = TestImage.createTempDir ()
        try
            let img = Path.Combine(root, "test.img")
            f root img
        finally
            TestImage.cleanupDir root

    [<Fact>]
    let ``extract recupere les fichiers et les repertoires de l'image`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "Bonjour Diplo"; @"dossier\sub.txt", "sous" ]
            let staging = Path.Combine(root, "staging")
            let n = FsImage.extract img staging false
            n |> should equal 2
            File.ReadAllText(Path.Combine(staging, "hello.txt")) |> should equal "Bonjour Diplo"
            File.ReadAllText(Path.Combine(staging, "dossier", "sub.txt")) |> should equal "sous")

    [<Fact>]
    let ``writeBack reecrit les fichiers modifies et ajoute les nouveaux`` () =
        run (fun root img ->
            TestImage.createFat img [ "hello.txt", "v1" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.WriteAllText(Path.Combine(staging, "hello.txt"), "v2")
            File.WriteAllText(Path.Combine(staging, "nouveau.txt"), "nouveau")
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.ReadAllText(Path.Combine(re, "hello.txt")) |> should equal "v2"
            File.ReadAllText(Path.Combine(re, "nouveau.txt")) |> should equal "nouveau")

    [<Fact>]
    let ``writeBack supprime les fichiers disparus du staging`` () =
        run (fun root img ->
            TestImage.createFat img [ "a.txt", "a"; "b.txt", "b" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            File.Delete(Path.Combine(staging, "a.txt"))
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            File.Exists(Path.Combine(re, "a.txt")) |> should equal false
            File.Exists(Path.Combine(re, "b.txt")) |> should equal true)

    [<Fact>]
    let ``writeBack vide la corbeille des repertoires supprimes du staging`` () =
        run (fun root img ->
            TestImage.createFat img [ @"dossier\a.txt", "a" ]
            let staging = Path.Combine(root, "staging")
            FsImage.extract img staging false |> ignore
            Directory.Delete(Path.Combine(staging, "dossier"), true)
            FsImage.writeBack img staging
            let re = Path.Combine(root, "re")
            FsImage.extract img re false |> ignore
            Directory.Exists(Path.Combine(re, "dossier")) |> should equal false)

    [<Fact>]
    let ``extract leve une exception sur un fichier sans systeme de fichiers`` () =
        run (fun root img ->
            File.WriteAllBytes(img, Array.create 4096 0uy)
            (fun () -> FsImage.extract img (Path.Combine(root, "s")) false |> ignore)
            |> should throw typeof<System.Exception>)

    [<Fact>]
    let ``writeBack leve une exception si l'image n'existe pas`` () =
        run (fun root _ ->
            let missing = Path.Combine(root, "missing.img")
            (fun () -> FsImage.writeBack missing (Path.Combine(root, "staging")))
            |> should throw typeof<System.IO.FileNotFoundException>)

    // ── Tests de FsImage.create ──────────────────────────────────────

    let private runCreate (f: string -> string -> string -> unit) =
        let root = TestImage.createTempDir ()
        try
            let src = Path.Combine(root, "src")
            let dest = Path.Combine(root, "dest", "test.vhd")
            Directory.CreateDirectory(src) |> ignore
            f root src dest
        finally
            TestImage.cleanupDir root

    let private sourceContents =
        [ "fichier.txt", "contenu du fichier"
          @"sous\dossier\fichier2.txt", "deuxieme fichier" ]

    let private writeSourceDir (src: string) =
        for (rel, content) in sourceContents do
            let parent = Path.GetDirectoryName(rel)
            if not (String.IsNullOrEmpty parent) then
                Directory.CreateDirectory(Path.Combine(src, parent)) |> ignore
            File.WriteAllText(Path.Combine(src, rel), content)

    [<Fact>]
    let ``create genere un fichier VHD existant et lisible`` () =
        runCreate (fun _ src dest ->
            writeSourceDir src
            let result = FsImage.create src dest DiskFormat.Vhd
            result |> should equal dest
            File.Exists(dest) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(dest), "re")
            FsImage.extract dest re false |> ignore
            File.ReadAllText(Path.Combine(re, "fichier.txt")) |> should equal "contenu du fichier"
            File.ReadAllText(Path.Combine(re, "sous", "dossier", "fichier2.txt")) |> should equal "deuxieme fichier")

    [<Fact>]
    let ``create genere un fichier VHDX et lisible`` () =
        runCreate (fun _ src dest ->
            let destVhdx = Path.ChangeExtension(dest, ".vhdx")
            writeSourceDir src
            let result = FsImage.create src destVhdx DiskFormat.Vhdx
            result |> should equal destVhdx
            File.Exists(destVhdx) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(destVhdx), "re")
            FsImage.extract destVhdx re false |> ignore
            File.ReadAllText(Path.Combine(re, "fichier.txt")) |> should equal "contenu du fichier")

    [<Fact>]
    let ``create genere un fichier VMDK et lisible`` () =
        runCreate (fun _ src dest ->
            let destVmdk = Path.ChangeExtension(dest, ".vmdk")
            writeSourceDir src
            let result = FsImage.create src destVmdk DiskFormat.Vmdk
            result |> should equal destVmdk
            File.Exists(destVmdk) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(destVmdk), "re")
            FsImage.extract destVmdk re false |> ignore
            File.ReadAllText(Path.Combine(re, "fichier.txt")) |> should equal "contenu du fichier")

    [<Fact>]
    let ``create genere un fichier VDI et lisible`` () =
        runCreate (fun _ src dest ->
            let destVdi = Path.ChangeExtension(dest, ".vdi")
            writeSourceDir src
            let result = FsImage.create src destVdi DiskFormat.Vdi
            result |> should equal destVdi
            File.Exists(destVdi) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(destVdi), "re")
            FsImage.extract destVdi re false |> ignore
            File.ReadAllText(Path.Combine(re, "fichier.txt")) |> should equal "contenu du fichier")

    [<Fact>]
    let ``create genere un fichier Raw et lisible`` () =
        runCreate (fun _ src dest ->
            let destRaw = Path.ChangeExtension(dest, ".img")
            writeSourceDir src
            let result = FsImage.create src destRaw DiskFormat.Raw
            result |> should equal destRaw
            File.Exists(destRaw) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(destRaw), "re")
            FsImage.extract destRaw re false |> ignore
            File.ReadAllText(Path.Combine(re, "fichier.txt")) |> should equal "contenu du fichier")

    [<Fact>]
    let ``create leve invalidArg si le repertoire source n'existe pas`` () =
        runCreate (fun root _ dest ->
            let missing = Path.Combine(root, "n'existe pas")
            (fun () -> FsImage.create missing dest DiskFormat.Vhd |> ignore)
            |> should throw typeof<System.ArgumentException>)

    [<Fact>]
    let ``create leve invalidArg si le format n'est pas supporte en creation`` () =
        runCreate (fun _ src dest ->
            writeSourceDir src
            (fun () -> FsImage.create src dest DiskFormat.Qcow2 |> ignore)
            |> should throw typeof<System.ArgumentException>)

    [<Fact>]
    let ``create cree le repertoire parent du fichier de destination`` () =
        runCreate (fun _ src dest ->
            let nestedDest = Path.Combine(Path.GetDirectoryName(dest), "sous", "dossier", "img.vhd")
            writeSourceDir src
            FsImage.create src nestedDest DiskFormat.Vhd |> ignore
            File.Exists(nestedDest) |> should equal true)

    [<Fact>]
    let ``create genere un fichier avec une taille minimale`` () =
        runCreate (fun _ src dest ->
            File.WriteAllText(Path.Combine(src, "tiny.txt"), "petit")
            FsImage.create src dest DiskFormat.Vhd |> ignore
            let info = FileInfo(dest)
            info.Length |> should be (greaterThan 0L))

    [<Fact>]
    let ``create accepte un repertoire source vide`` () =
        runCreate (fun _ src dest ->
            let result = FsImage.create src dest DiskFormat.Vhd
            File.Exists(dest) |> should equal true)

    [<Fact>]
    let ``create gere les caracteres speciaux dans les noms de fichiers`` () =
        runCreate (fun _ src dest ->
            File.WriteAllText(Path.Combine(src, "fichier avec espaces.txt"), "espaces")
            File.WriteAllText(Path.Combine(src, "données-françaises.txt"), "accents")
            File.WriteAllText(Path.Combine(src, "fichier-v2.1.0_beta.txt"), "version")
            FsImage.create src dest DiskFormat.Vhd |> ignore
            File.Exists(dest) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(dest), "re")
            FsImage.extract dest re false |> ignore
            File.Exists(Path.Combine(re, "fichier avec espaces.txt")) |> should equal true
            File.Exists(Path.Combine(re, "données-françaises.txt")) |> should equal true
            File.Exists(Path.Combine(re, "fichier-v2.1.0_beta.txt")) |> should equal true)

    [<Fact>]
    let ``create gere les repertoires imbriques profondement`` () =
        runCreate (fun _ src dest ->
            let deepPath = Path.Combine(src, "a", "b", "c", "d", "e")
            Directory.CreateDirectory(deepPath) |> ignore
            File.WriteAllText(Path.Combine(deepPath, "profond.txt"), "niveau 5")
            FsImage.create src dest DiskFormat.Vhd |> ignore
            let re = Path.Combine(Path.GetDirectoryName(dest), "re")
            FsImage.extract dest re false |> ignore
            File.ReadAllText(Path.Combine(re, "a", "b", "c", "d", "e", "profond.txt")) |> should equal "niveau 5")

    [<Fact>]
    let ``create gere un gros fichier`` () =
        runCreate (fun _ src dest ->
            let bigFile = Path.Combine(src, "gros.bin")
            let data = Array.create (10 * 1024 * 1024) 0xABuy
            File.WriteAllBytes(bigFile, data)
            FsImage.create src dest DiskFormat.Vhd |> ignore
            File.Exists(dest) |> should equal true
            let info = FileInfo(dest)
            info.Length |> should be (greaterThan (int64 data.Length)))

    [<Fact>]
    let ``create gere les caracteres CJK dans les noms de fichiers`` () =
        runCreate (fun _ src dest ->
            File.WriteAllText(Path.Combine(src, "テスト.txt"), "japonais")
            File.WriteAllText(Path.Combine(src, "测试.txt"), "chinois")
            File.WriteAllText(Path.Combine(src, "한국어.txt"), "coréen")
            FsImage.create src dest DiskFormat.Vhd |> ignore
            let re = Path.Combine(Path.GetDirectoryName(dest), "re")
            FsImage.extract dest re false |> ignore
            File.Exists(Path.Combine(re, "テスト.txt")) |> should equal true
            File.Exists(Path.Combine(re, "测试.txt")) |> should equal true
            File.Exists(Path.Combine(re, "한국어.txt")) |> should equal true
            File.ReadAllText(Path.Combine(re, "テスト.txt")) |> should equal "japonais")

    [<Fact>]
    let ``create gere les fichiers en lecture seule`` () =
        runCreate (fun _ src dest ->
            let roFile = Path.Combine(src, "readonly.txt")
            File.WriteAllText(roFile, "protégé")
            File.SetAttributes(roFile, FileAttributes.ReadOnly)
            FsImage.create src dest DiskFormat.Vhd |> ignore
            File.Exists(dest) |> should equal true
            let re = Path.Combine(Path.GetDirectoryName(dest), "re")
            FsImage.extract dest re false |> ignore
            File.ReadAllText(Path.Combine(re, "readonly.txt")) |> should equal "protégé"
            File.SetAttributes(roFile, FileAttributes.Normal))

    [<Fact>]
    let ``create gere les liens symboliques NTFS`` () =
        runCreate (fun _ src dest ->
            File.WriteAllText(Path.Combine(src, "cible.txt"), "données")
            let linkPath = Path.Combine(src, "lien.txt")
            try
                File.CreateSymbolicLink(linkPath, Path.Combine(src, "cible.txt")) |> ignore
                FsImage.create src dest DiskFormat.Vhd |> ignore
                File.Exists(dest) |> should equal true
            with :? PlatformNotSupportedException -> ())
