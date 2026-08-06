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

    [<Fact>]
    let ``poll signale la sortie standard prête en écriture`` () =
        let image = ElfTest.create ElfTest.pollCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``ppoll relaie vers poll`` () =
        let image = ElfTest.create ElfTest.ppollCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``select marque la sortie standard comme prête à écrire`` () =
        let image = ElfTest.create ElfTest.selectCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``pread64 lit à une position donnée sans déplacer le curseur`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-pread-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "Bonjour")
            let image = ElfTest.create (ElfTest.preadCode path) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            text |> should equal "njour"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``pwrite64 écrit à une position donnée sans déplacer le curseur`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-pwrite-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "abcdef")
            let image = ElfTest.create (ElfTest.pwriteCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.ReadAllText path |> should equal "abhief"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``sendfile copie le contenu d'un fichier vers la sortie standard`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-sendfile-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "Bonjour")
            let image = ElfTest.create (ElfTest.sendfileCode path) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            text |> should equal "Bonjour"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``getdents énumère le contenu d'un répertoire`` () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-getdents-" + Guid.NewGuid().ToString("N"))
        try
            Directory.CreateDirectory dir |> ignore
            File.WriteAllText(Path.Combine(dir, "f.txt"), "x")
            let image = ElfTest.create (ElfTest.getdentsCode dir) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)

    [<Fact>]
    let ``truncate réduit la taille du fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-truncate-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "0123456789")
            let image = ElfTest.create (ElfTest.truncateCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            FileInfo(path).Length |> should equal 3L
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``symlink crée un lien symbolique`` () =
        let target = Path.Combine(Path.GetTempPath(), "diplo-symlink-target-" + Guid.NewGuid().ToString("N"))
        let link = Path.Combine(Path.GetTempPath(), "diplo-symlink-link-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(target, "cible")
            let image = ElfTest.create (ElfTest.symlinkCode target link) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            if File.Exists link then
                (new FileInfo(link)).LinkTarget |> should equal target
        finally
            if File.Exists target then File.Delete target
            if File.Exists link then File.Delete link

    [<Fact>]
    let ``readlink lit la cible d'un lien symbolique`` () =
        let target = Path.Combine(Path.GetTempPath(), "diplo-readlink-target-" + Guid.NewGuid().ToString("N"))
        let link = Path.Combine(Path.GetTempPath(), "diplo-readlink-link-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(target, "cible")
            try
                File.CreateSymbolicLink(link, target) |> ignore
            with :? UnauthorizedAccessException ->
                ()
            let image = ElfTest.create (ElfTest.readlinkCode link) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            if File.Exists link then
                text |> should equal target
        finally
            if File.Exists target then File.Delete target
            if File.Exists link then File.Delete link

    [<Fact>]
    let ``getresuid et getresgid exposent l'identité courante`` () =
        let imageUid = ElfTest.create ElfTest.getresuidCode 0x400000UL 0x400000UL
        let codeUid, _ = runWithStdout imageUid [||]
        codeUid |> should equal 0
        let imageGid = ElfTest.create ElfTest.getresgidCode 0x400000UL 0x400000UL
        let codeGid, _ = runWithStdout imageGid [||]
        codeGid |> should equal 0

    [<Fact>]
    let ``getcpu expose le processeur et le nœud`` () =
        let image = ElfTest.create ElfTest.getcpuCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``umask retourne puis met à jour le masque courant`` () =
        let image = ElfTest.create ElfTest.umaskCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``getrusage remplit le tampon de ressources`` () =
        let image = ElfTest.create ElfTest.getrusageCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``sysinfo remplit uptime, mémoire et processus`` () =
        let image = ElfTest.create ElfTest.sysinfoCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``times remplit le tableau des temps CPU`` () =
        let image = ElfTest.create ElfTest.timesCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``statfs expose les caractéristiques du système de fichiers`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-statfs-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "x")
            let image = ElfTest.create (ElfTest.statfsCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``fstatfs expose les caractéristiques via le descripteur`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-fstatfs-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "x")
            let image = ElfTest.create (ElfTest.fstatfsCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``futex FUTEX_WAKE réveille un observateur`` () =
        let image = ElfTest.create ElfTest.futexCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``clock_getres remplit la résolution et rejette les horloges inconnues`` () =
        let image = ElfTest.create ElfTest.clockGetresCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``clock_nanosleep accepte une attente nulle`` () =
        let image = ElfTest.create ElfTest.clockNanosleepCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``prlimit64 expose la limite RLIMIT_NOFILE`` () =
        let image = ElfTest.create ElfTest.prlimit64Code 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``sched_getaffinity remplit le masque d'affinité`` () =
        let image = ElfTest.create ElfTest.schedGetaffinityCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mincore signale les pages résidentes`` () =
        let image = ElfTest.create ElfTest.mincoreCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``madvise accepte les conseils de pagination`` () =
        let image = ElfTest.create ElfTest.madviseCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``msync synchronise la région mappée`` () =
        let image = ElfTest.create ElfTest.msyncCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mremap déplace une région en préservant son contenu`` () =
        let image = ElfTest.create ElfTest.mremapCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``getitimer remplit le tampon de minuterie`` () =
        let image = ElfTest.create ElfTest.getitimerCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``fdatasync synchronise les données du fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-fdatasync-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "data")
            let image = ElfTest.create (ElfTest.fdatasyncCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.ReadAllText path |> should equal "data"
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``mkdirat crée un répertoire relatif au descripteur`` () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-mkdirat-" + Guid.NewGuid().ToString("N"))
        try
            let image = ElfTest.create (ElfTest.mkdiratCode dir) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            Directory.Exists dir |> should equal true
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)

    [<Fact>]
    let ``unlinkat supprime un fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-unlinkat-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "x")
            let image = ElfTest.create (ElfTest.unlinkatCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.Exists path |> should equal false
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``renameat renomme un fichier`` () =
        let oldPath = Path.Combine(Path.GetTempPath(), "diplo-renameat-old-" + Guid.NewGuid().ToString("N"))
        let newPath = Path.Combine(Path.GetTempPath(), "diplo-renameat-new-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(oldPath, "x")
            let image = ElfTest.create (ElfTest.renameatCode oldPath newPath) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            File.Exists newPath |> should equal true
            File.Exists oldPath |> should equal false
        finally
            if File.Exists oldPath then File.Delete oldPath
            if File.Exists newPath then File.Delete newPath

    [<Fact>]
    let ``newfstatat remplit la structure stat`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-newfstatat-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "Bonjour")
            let image = ElfTest.create (ElfTest.newfstatatCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``symlinkat crée un lien symbolique relatif à un descripteur`` () =
        let target = Path.Combine(Path.GetTempPath(), "diplo-symlinkat-target-" + Guid.NewGuid().ToString("N"))
        let link = Path.Combine(Path.GetTempPath(), "diplo-symlinkat-link-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(target, "cible")
            let image = ElfTest.create (ElfTest.symlinkatCode target link) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
            if File.Exists link then
                (new FileInfo(link)).LinkTarget |> should equal target
        finally
            if File.Exists target then File.Delete target
            if File.Exists link then File.Delete link

    [<Fact>]
    let ``readlinkat lit la cible d'un lien symbolique via un descripteur`` () =
        let target = Path.Combine(Path.GetTempPath(), "diplo-readlinkat-target-" + Guid.NewGuid().ToString("N"))
        let link = Path.Combine(Path.GetTempPath(), "diplo-readlinkat-link-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(target, "cible")
            try
                File.CreateSymbolicLink(link, target) |> ignore
            with :? UnauthorizedAccessException ->
                ()
            let image = ElfTest.create (ElfTest.readlinkatCode link) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            if File.Exists link then
                text |> should equal target
        finally
            if File.Exists target then File.Delete target
            if File.Exists link then File.Delete link

    [<Fact>]
    let ``fchmodat applique un mode sur un fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-fchmodat-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "x")
            let image = ElfTest.create (ElfTest.fchmodatCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``faccessat vérifie l'accès à un fichier`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-faccessat-" + Guid.NewGuid().ToString("N"))
        try
            File.WriteAllText(path, "x")
            let image = ElfTest.create (ElfTest.faccessatCode path) 0x400000UL 0x400000UL
            let code, _ = runWithStdout image [||]
            code |> should equal 0
        finally
            if File.Exists path then File.Delete path

    [<Fact>]
    let ``pselect6 accepte un ensemble de descripteurs vide`` () =
        let image = ElfTest.create ElfTest.pselect6Code 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``/dev/null renvoie une fin de fichier en lecture et ignore l'écriture`` () =
        let image = ElfTest.create ElfTest.devNullCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``/dev/zero renvoie des octets nuls en lecture`` () =
        let image = ElfTest.create ElfTest.devZeroCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``/dev/random renvoie des octets aléatoires en lecture`` () =
        let image = ElfTest.create ElfTest.devRandomCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``/proc/self/cmdline expose la ligne de commande du processus`` () =
        let image = ElfTest.create ElfTest.procSelfCmdlineCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [|"/bin/test"|]
        code |> should equal 0

    [<Fact>]
    let ``/proc/self s'énumère comme un répertoire contenant ses entrées`` () =
        let image = ElfTest.create ElfTest.procSelfGetdentsCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``readlink /proc/self/exe pointe vers l'exécutable courant`` () =
        let image = ElfTest.create ElfTest.readlinkExeCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [|"/bin/diplo"|]
        code |> should equal 0
        text |> should equal "/bin/diplo"

    [<Fact>]
    let ``stat /dev/null reconnaît un périphérique de caractères`` () =
        let image = ElfTest.create ElfTest.statDevNullCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0
