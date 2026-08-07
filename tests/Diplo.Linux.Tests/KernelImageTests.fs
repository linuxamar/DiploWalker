namespace Diplo.Linux.Tests

open System
open System.Text
open Xunit
open FsUnit.Xunit
open Diplo.Linux

/// Tests de l'extraction et de la décompression zstd du noyau (module KernelImage).
module KernelImageTests =

    /// Texte répété pour produire un échantillon compressible.
    let private sampleText =
        "Bonjour le monde ! Test zstd pour Diplo.Linux. "

    /// Contenu original de test (octets UTF-8 du texte répété).
    let private sampleContent : byte[] =
        Encoding.UTF8.GetBytes (String.replicate 100 sampleText)

    /// Compresse un contenu en une frame zstd à l'aide de ZstdSharp.
    let private compress (content : byte[]) : byte[] =
        use c = new ZstdSharp.Compressor()
        let dest = Array.zeroCreate<byte> (content.Length * 2 + 256)
        let n = c.Wrap(content, 0, content.Length, dest, 0, dest.Length)
        dest[0 .. n - 1]

    /// Frame zstd produite par la compression de l'échantillon de test.
    let private sampleFrame : byte[] = compress sampleContent

    [<Fact>]
    let ``findFrameStart localise une frame zstd placée au début des données`` () =
        KernelImage.findFrameStart sampleFrame |> should equal 0

    [<Fact>]
    let ``findFrameStart localise une frame zstd précédée d'octets de remplissage`` () =
        let padded = Array.append (Array.zeroCreate<byte> 8) sampleFrame
        KernelImage.findFrameStart padded |> should equal 8

    [<Fact>]
    let ``findFrameStart lève ArgumentException quand le magic zstd est absent`` () =
        let data = [| 1uy; 2uy; 3uy; 4uy |]
        (fun () -> KernelImage.findFrameStart data |> ignore) |> should throw typeof<ArgumentException>

    [<Fact>]
    let ``decompress restitue le contenu original compressé en zstd`` () =
        let roundTrip = KernelImage.decompress sampleFrame 0
        roundTrip |> should equal sampleContent

    [<Fact>]
    let ``decompress ignore les données résiduelles après la fin de la frame`` () =
        let data = Array.append sampleFrame [| 1uy; 2uy; 3uy |]
        let roundTrip = KernelImage.decompress data 0
        roundTrip |> should equal sampleContent

    [<Fact>]
    let ``decompress lève ArgumentException sur une frame tronquée`` () =
        let truncated = sampleFrame[0 .. sampleFrame.Length / 2 - 1]
        (fun () -> KernelImage.decompress truncated 0 |> ignore) |> should throw typeof<ArgumentException>

    [<Fact>]
    let ``decompressKernel extrait et décompresse le payload zstd d'une image de démarrage`` () =
        let image =
            Array.append
                (Array.zeroCreate<byte> 16)
                (Array.append sampleFrame [| 0xEEuy; 0xFFuy |])

        let kernel = KernelImage.decompressKernel image
        kernel |> should equal sampleContent
