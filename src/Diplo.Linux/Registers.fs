namespace Diplo.Linux

/// Registres généraux du processeur x86-64 émulé.
type Registers() =
    let mutable rax = 0UL
    let mutable rbx = 0UL
    let mutable rcx = 0UL
    let mutable rdx = 0UL
    let mutable rsi = 0UL
    let mutable rdi = 0UL
    let mutable rbp = 0UL
    let mutable rsp = 0UL
    let mutable r8 = 0UL
    let mutable r9 = 0UL
    let mutable r10 = 0UL
    let mutable r11 = 0UL
    let mutable r12 = 0UL
    let mutable r13 = 0UL
    let mutable r14 = 0UL
    let mutable r15 = 0UL
    let mutable rip = 0UL
    let mutable rflags = 2UL
    let xmm = Array.init 16 (fun _ -> Array.zeroCreate 16)

    member _.RAX with get () = rax and set v = rax <- v
    member _.RBX with get () = rbx and set v = rbx <- v
    member _.RCX with get () = rcx and set v = rcx <- v
    member _.RDX with get () = rdx and set v = rdx <- v
    member _.RSI with get () = rsi and set v = rsi <- v
    member _.RDI with get () = rdi and set v = rdi <- v
    member _.RBP with get () = rbp and set v = rbp <- v
    member _.RSP with get () = rsp and set v = rsp <- v
    member _.R8 with get () = r8 and set v = r8 <- v
    member _.R9 with get () = r9 and set v = r9 <- v
    member _.R10 with get () = r10 and set v = r10 <- v
    member _.R11 with get () = r11 and set v = r11 <- v
    member _.R12 with get () = r12 and set v = r12 <- v
    member _.R13 with get () = r13 and set v = r13 <- v
    member _.R14 with get () = r14 and set v = r14 <- v
    member _.R15 with get () = r15 and set v = r15 <- v
    member _.RIP with get () = rip and set v = rip <- v
    member _.RFLAGS with get () = rflags and set v = rflags <- v

    /// Registre XMM d'index donné (128 bits = 16 octets).
    member _.Xmm(i : int) : byte[] = xmm[i]

    /// Duplique l'ensemble des registres (copie complète, XMM inclus).
    member _.Clone() : Registers =
        let copy = Registers()
        copy.RAX <- rax
        copy.RBX <- rbx
        copy.RCX <- rcx
        copy.RDX <- rdx
        copy.RSI <- rsi
        copy.RDI <- rdi
        copy.RBP <- rbp
        copy.RSP <- rsp
        copy.R8 <- r8
        copy.R9 <- r9
        copy.R10 <- r10
        copy.R11 <- r11
        copy.R12 <- r12
        copy.R13 <- r13
        copy.R14 <- r14
        copy.R15 <- r15
        copy.RIP <- rip
        copy.RFLAGS <- rflags
        for i in 0 .. 15 do
            Array.blit xmm[i] 0 (copy.Xmm i) 0 16
        copy

/// Module utilitaire d'accès aux registres.
module Registers =

    /// Crée un jeu de registres initialisé (RFLAGS = 2, bit 1 toujours à 1).
    let create () = Registers()

    /// Retourne la valeur 64 bits du registre d'index donné (encodage x86).
    let get64 (r : Registers) (index : int) : uint64 =
        match index with
        | 0 -> r.RAX
        | 1 -> r.RCX
        | 2 -> r.RDX
        | 3 -> r.RBX
        | 4 -> r.RSP
        | 5 -> r.RBP
        | 6 -> r.RSI
        | 7 -> r.RDI
        | 8 -> r.R8
        | 9 -> r.R9
        | 10 -> r.R10
        | 11 -> r.R11
        | 12 -> r.R12
        | 13 -> r.R13
        | 14 -> r.R14
        | 15 -> r.R15
        | _ -> invalidArg "index" $"Registre invalide : {index}"

    /// Affecte la valeur 64 bits du registre d'index donné (encodage x86).
    let set64 (r : Registers) (index : int) (value : uint64) =
        match index with
        | 0 -> r.RAX <- value
        | 1 -> r.RCX <- value
        | 2 -> r.RDX <- value
        | 3 -> r.RBX <- value
        | 4 -> r.RSP <- value
        | 5 -> r.RBP <- value
        | 6 -> r.RSI <- value
        | 7 -> r.RDI <- value
        | 8 -> r.R8 <- value
        | 9 -> r.R9 <- value
        | 10 -> r.R10 <- value
        | 11 -> r.R11 <- value
        | 12 -> r.R12 <- value
        | 13 -> r.R13 <- value
        | 14 -> r.R14 <- value
        | 15 -> r.R15 <- value
        | _ -> invalidArg "index" $"Registre invalide : {index}"

    /// Retourne la valeur 32 bits (zero-extendue) du registre d'index donné.
    let get32 (r : Registers) (index : int) = get64 r index &&& 0xFFFFFFFFUL

    /// Affecte la valeur 32 bits du registre d'index donné (zero-extendue).
    let set32 (r : Registers) (index : int) (value : uint32) =
        set64 r index (uint64 value)

    /// Retourne l'octet bas du registre d'index donné (encodage x86).
    let get8 (r : Registers) (index : int) : byte =
        get64 r index |> byte

    /// Affecte l'octet bas du registre d'index donné (le reste est conservé).
    let set8 (r : Registers) (index : int) (value : byte) =
        let full = get64 r index
        set64 r index ((full &&& 0xFFFFFFFFFFFFFF00UL) ||| uint64 value)

    /// Retourne le contenu 16 octets du registre XMM d'index donné.
    let getXmm (r : Registers) (index : int) : byte[] = r.Xmm index

    /// Affecte le contenu 16 octets du registre XMM d'index donné.
    let setXmm (r : Registers) (index : int) (value : byte[]) =
        Array.blit value 0 (r.Xmm index) 0 16
