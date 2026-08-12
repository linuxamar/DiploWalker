namespace Diplo.Disk.Tests

open System
open System.IO
open DiscUtils
open DiscUtils.Fat
open DiscUtils.Partitions
open DiscUtils.Streams
open Diplo.Disk

/// Fabrique d'images disque de test : formatage FAT via DiscUtils.
module TestImage =

    /// Flux qcow2 de test : tolère l'appel SetLength de Raw.Disk.Initialize
    /// (la taille virtuelle est fixée par l'en-tête, aucun redimensionnement).
    type private Qcow2TestStream(path: string, access: FileAccess) =
        inherit Qcow2Stream(path, access)
        override _.SetLength _ = ()

    let private putBe16 (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte (v >>> 8)
        d.[o + 1] <- byte v

    let private putBe32 (d: byte[]) (o: int) (v: int) =
        d.[o] <- byte (v >>> 24)
        d.[o + 1] <- byte (v >>> 16)
        d.[o + 2] <- byte (v >>> 8)
        d.[o + 3] <- byte v

    let private putBe64 (d: byte[]) (o: int) (v: int64) =
        d.[o] <- byte (v >>> 56)
        d.[o + 1] <- byte (v >>> 48)
        d.[o + 2] <- byte (v >>> 40)
        d.[o + 3] <- byte (v >>> 32)
        d.[o + 4] <- byte (v >>> 24)
        d.[o + 5] <- byte (v >>> 16)
        d.[o + 6] <- byte (v >>> 8)
        d.[o + 7] <- byte v

    /// Écrit un squelette d'image qcow2 version 2 conforme à la spécification
    /// QEMU : en-tête (cluster 0), table L1 (cluster 1), table de refcounts
    /// (cluster 2) et blocs de refcounts pré-alloués (clusters 3..11) avec un
    /// refcount de 1 pour les clusters structurels. Image vide, taille
    /// virtuelle 64 Mo (ou `virtualSize`), clusters de 4 Ko.
    let private writeQcow2Skeleton (path: string) (virtualSize: int64) =
        let clusterSize = 4096L
        let l1Size = int (virtualSize / (clusterSize * 512L))
        let l1Offset = clusterSize
        let refcountTableOffset = 2L * clusterSize
        let refcountsPerBlock = 2048
        let dataClusters = virtualSize / clusterSize
        let usedClusters = dataClusters + 12L
        let blocks = int ((usedClusters + int64 refcountsPerBlock - 1L) / int64 refcountsPerBlock)
        let firstBlockCluster = 3L

        use fs = new FileStream(path, FileMode.Create, FileAccess.Write)

        let header = Array.zeroCreate<byte> 4096
        header.[0] <- 0x51uy
        header.[1] <- 0x46uy
        header.[2] <- 0x49uy
        header.[3] <- 0xFBuy
        putBe32 header 4 2
        putBe32 header 20 12
        putBe64 header 24 virtualSize
        putBe32 header 36 l1Size
        putBe64 header 40 l1Offset
        putBe64 header 48 refcountTableOffset
        putBe32 header 56 1
        fs.Write(header, 0, header.Length)

        let l1 = Array.zeroCreate<byte> 4096
        fs.Write(l1, 0, l1.Length)

        let refcountTable = Array.zeroCreate<byte> 4096
        for i in 0 .. blocks - 1 do
            putBe64 refcountTable (i * 8) ((firstBlockCluster + int64 i) * clusterSize)
        fs.Write(refcountTable, 0, refcountTable.Length)

        for i in 0 .. blocks - 1 do
            let block = Array.zeroCreate<byte> 4096
            if i = 0 then
                for n in 0 .. 11 do
                    putBe16 block (n * 2) 1
            fs.Write(block, 0, block.Length)

    /// Écrit le contenu (chemin relatif, contenu texte) dans un système de
    /// fichiers FAT.
    let private writeContents (fat: FatFileSystem) (contents: (string * string) list) =
        for (relPath, content) in contents do
            let parent = Path.GetDirectoryName(relPath)
            if not (String.IsNullOrEmpty parent) && not (fat.DirectoryExists parent) then
                fat.CreateDirectory(parent)
            use w = fat.OpenFile(relPath, FileMode.Create, FileAccess.ReadWrite)
            use sw = new StreamWriter(w)
            sw.Write(content)
            sw.Flush()

    /// Crée une image disque FAT 64 Mo, avec table de partitions BIOS, et y
    /// écrit le contenu (chemin relatif, contenu texte) fourni.
    let createFat (path: string) (contents: (string * string) list) =
        use fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite)
        use disk = Raw.Disk.Initialize(fs, Ownership.None, 64L * 1024L * 1024L)
        BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat) |> ignore
        use fat = FatFileSystem.FormatPartition(disk, 0, "DIPLO")
        writeContents fat contents

    /// Crée une image disque qcow2 (version 2) de `virtualSize` Mo contenant
    /// un système de fichiers FAT formaté via DiscUtils, puis y écrit le
    /// contenu (chemin relatif, contenu texte) fourni.
    let createQcow2WithSize (path: string) (virtualSizeMb: int64) (contents: (string * string) list) =
        writeQcow2Skeleton path (virtualSizeMb * 1024L * 1024L)
        use stream = new Qcow2TestStream(path, FileAccess.ReadWrite)
        use disk = Raw.Disk.Initialize(stream, Ownership.None, virtualSizeMb * 1024L * 1024L)
        BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat) |> ignore
        use fat = FatFileSystem.FormatPartition(disk, 0, "DIPLO")
        writeContents fat contents

    /// Crée une image disque qcow2 (version 2) contenant un système de
    /// fichiers FAT 64 Mo formaté via DiscUtils, puis y écrit le contenu
    /// (chemin relatif, contenu texte) fourni.
    let createQcow2 (path: string) (contents: (string * string) list) =
        createQcow2WithSize path 64L (contents)

    /// Crée une image qcow2 (version 2) vide de `virtualSizeMo` Mo, sans
    /// système de fichiers (squelette seul) : utile pour les tests de
    /// redimensionnement sans coût de formatage.
    let createEmptyQcow2 (path: string) (virtualSizeMb: int64) =
        writeQcow2Skeleton path (virtualSizeMb * 1024L * 1024L)

    /// Répertoire temporaire unique.
    let createTempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-disk-test-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        dir

    /// Suppression récursive tolérante aux erreurs.
    let cleanupDir (dir: string) =
        try if Directory.Exists(dir) then Directory.Delete(dir, true) with _ -> ()
