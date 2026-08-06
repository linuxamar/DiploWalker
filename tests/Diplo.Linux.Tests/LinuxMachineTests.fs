namespace Diplo.Linux.Tests

open System
open System.IO
open System.Text
open Xunit
open FsUnit.Xunit
open Diplo.Linux

module LinuxMachineTests =

    let private runWithStdout (image : byte[]) (args : string[]) : int * string =
        use machine = new LinuxMachine(image, args)
        use buffer = new MemoryStream()
        machine.StandardOutput <- buffer
        let code = machine.Run()
        let text = Encoding.UTF8.GetString(buffer.ToArray())
        code, text

    [<Fact>]
    let ``Le programme « hello » écrit sur la sortie standard et retourne 42`` () =
        let image = ElfTest.create ElfTest.helloWorldCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [||]
        code |> should equal 42
        text |> should equal "Hello, Linux\n"

    [<Fact>]
    let ``Le programme reçoit ses arguments sur la pile`` () =
        let image = ElfTest.create ElfTest.argvCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [| "prog"; "bonjour" |]
        code |> should equal 0
        text |> should equal "bonjour"

    [<Fact>]
    let ``Un fichier non ELF déclenche une erreur de validation`` () =
        let image = Array.zeroCreate 128
        image[0] <- 0x00uy
        image[1] <- 0x41uy
        use machine = new LinuxMachine(image, [||])
        (fun () -> machine.Run() |> ignore) |> should throw typeof<ArgumentException>

    let private runWithConsole (code : byte[]) : int * string =
        use machine = new LinuxMachine(ElfTest.create code 0x400000UL 0x400000UL, [||])
        use output = new MemoryStream()
        machine.ConsoleOutput <- output
        let code = machine.Run()
        let text = Encoding.UTF8.GetString(output.ToArray())
        code, text

    [<Fact>]
    let ``La console série reçoit l'écriture vers /dev/console`` () =
        let code, text = runWithConsole ElfTest.consoleWriteCode
        code |> should equal 0
        text |> should equal "Bonjour console\n"

    [<Fact>]
    let ``La console série relaie la lecture depuis /dev/console`` () =
        use machine = new LinuxMachine(ElfTest.create ElfTest.consoleEchoCode 0x400000UL 0x400000UL, [||])
        use input = new MemoryStream(Encoding.UTF8.GetBytes "Salut")
        use output = new MemoryStream()
        machine.ConsoleInput <- input
        machine.ConsoleOutput <- output
        let code = machine.Run()
        code |> should equal 0
        Encoding.UTF8.GetString(output.ToArray()) |> should equal "Salut"

    [<Fact>]
    let ``mmap anonyme retourne une adresse alignée sur une page`` () =
        let image = ElfTest.create ElfTest.mmapAnonCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mmap, mprotect et munmap se déroulent sans erreur`` () =
        let image = ElfTest.create ElfTest.mmapLifecycleCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mmap lit le contenu d'un fichier`` () =
        let path = Path.GetTempFileName()
        try
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes "Bonjour")
            let image = ElfTest.create (ElfTest.mmapFileCode path) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            text |> should equal "Bonjou"
        finally
            File.Delete path

    [<Fact>]
    let ``fork : l'enfant sort avec 3 et le parent avec 7`` () =
        let image = ElfTest.create ElfTest.forkCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 3

    [<Fact>]
    let ``wait4 sans enfant terminé retourne -ECHILD`` () =
        let image = ElfTest.create ElfTest.wait4EmptyCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``getrandom remplit le tampon demandé`` () =
        let image = ElfTest.create ElfTest.getrandomCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``pipe2 fait transiter « ping » d'une extrémité à l'autre`` () =
        let image = ElfTest.create ElfTest.pipe2Code 0x400000UL 0x400000UL
        let code, text = runWithStdout image [||]
        code |> should equal 0
        text |> should equal "ping"

    [<Fact>]
    let ``dup2 duplique la sortie standard sur un nouveau descripteur`` () =
        let image = ElfTest.create ElfTest.dup2Code 0x400000UL 0x400000UL
        let code, text = runWithStdout image [||]
        code |> should equal 0
        text |> should equal "duplo"

    [<Fact>]
    let ``fcntl F_DUPFD duplique la sortie standard sur un descripteur libre`` () =
        let image = ElfTest.create ElfTest.fcntlDupCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [||]
        code |> should equal 0
        text |> should equal "fd"

    [<Fact>]
    let ``ioctl sur une requête non gérée retourne -ENOTTY`` () =
        let image = ElfTest.create ElfTest.ioctlCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``getrlimit RLIMIT_NOFILE expose 4096 descripteurs`` () =
        let image = ElfTest.create ElfTest.getrlimitCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``chdir puis getcwd retournent le nouveau répertoire`` () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-chdir-" + Guid.NewGuid().ToString("N"))
        try
            Directory.CreateDirectory dir |> ignore
            use machine = new LinuxMachine(ElfTest.create (ElfTest.chdirCode dir) 0x400000UL 0x400000UL, [||])
            use output = new MemoryStream()
            machine.StandardOutput <- output
            let code = machine.Run()
            code |> should equal 0
            Encoding.UTF8.GetString(output.ToArray()) |> should equal dir
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)

    [<Fact>]
    let ``gettid retourne l'identifiant du thread`` () =
        let image = ElfTest.create ElfTest.gettidCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``time écrit l'heure Unix dans le tampon`` () =
        use machine = new LinuxMachine(ElfTest.create ElfTest.timeCode 0x400000UL 0x400000UL, [||])
        use output = new MemoryStream()
        machine.StandardOutput <- output
        let code = machine.Run()
        code |> should equal 0
        let bytes = output.ToArray()
        bytes.Length |> should equal 8
        let guestTime = BitConverter.ToUInt64 (bytes, 0)
        let now = uint64 (DateTimeOffset.UtcNow.ToUnixTimeSeconds ())
        abs (int64 guestTime - int64 now) |> should be (lessThan 5L)

    [<Fact>]
    let ``setrlimit accepte RLIMIT_NOFILE`` () =
        let image = ElfTest.create ElfTest.setrlimitCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``creat crée un fichier et permet l'écriture`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-creat-" + Guid.NewGuid().ToString("N"))
        try
            let image = ElfTest.create (ElfTest.creatCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.ReadAllText path |> should equal "hi"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``fsync vide les tampons du fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-fsync-" + Guid.NewGuid().ToString("N"))
        try
            let image = ElfTest.create (ElfTest.fsyncCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.ReadAllText path |> should equal "data"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``ftruncate réduit la taille du fichier à zéro`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-ftruncate-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "some-content")
            let image = ElfTest.create (ElfTest.ftruncateCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            FileInfo(path).Length |> should equal 0L
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``fchdir puis getcwd retournent le répertoire du descripteur`` () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-fchdir-" + Guid.NewGuid().ToString("N"))
        try
            Directory.CreateDirectory dir |> ignore
            let image = ElfTest.create (ElfTest.fchdirCode dir) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            text |> should equal dir
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)
