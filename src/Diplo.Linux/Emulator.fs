namespace Diplo.Linux

open System
open System.Numerics

/// Interpréteur x86-64 du sous-ensemble décodé.
module Emulator =

    [<Literal>]
    let private FlagCf = 0x1UL

    [<Literal>]
    let private FlagPf = 0x4UL

    [<Literal>]
    let private FlagZf = 0x40UL

    [<Literal>]
    let private FlagSf = 0x80UL

    [<Literal>]
    let private FlagOf = 0x800UL

    [<Literal>]
    let private FlagDf = 0x400UL

    let private maskFor (width : int) =
        match width with
        | 8 -> 0xFFUL
        | 16 -> 0xFFFFUL
        | 32 -> 0xFFFFFFFFUL
        | _ -> 0xFFFFFFFFFFFFFFFFUL

    let private signBitFor (width : int) =
        match width with
        | 8 -> 0x80UL
        | 16 -> 0x8000UL
        | 32 -> 0x80000000UL
        | _ -> 0x8000000000000000UL

    let private setFlags (regs : Registers) (width : int) (result : uint64) (cf : bool) (ovf : bool) =
        let mask = maskFor width
        let signBit = signBitFor width
        let mutable flags = regs.RFLAGS &&& ~~~(FlagCf ||| FlagPf ||| FlagZf ||| FlagSf ||| FlagOf)
        flags <- flags ||| 0x2UL
        if result = 0UL then flags <- flags ||| FlagZf
        if (result &&& signBit) <> 0UL then flags <- flags ||| FlagSf
        if BitOperations.PopCount (uint (result &&& 0xFFUL)) % 2 = 0 then flags <- flags ||| FlagPf
        if cf then flags <- flags ||| FlagCf
        if ovf then flags <- flags ||| FlagOf
        regs.RFLAGS <- flags

    let private condHolds (regs : Registers) (cond : FlagCond) : bool =
        let cf = regs.RFLAGS &&& FlagCf <> 0UL
        let zf = regs.RFLAGS &&& FlagZf <> 0UL
        let sf = regs.RFLAGS &&& FlagSf <> 0UL
        let ov = regs.RFLAGS &&& FlagOf <> 0UL
        let pf = regs.RFLAGS &&& FlagPf <> 0UL
        match cond with
        | JO -> ov
        | JNO -> not ov
        | JB -> cf
        | JAE -> not cf
        | JE -> zf
        | JNE -> not zf
        | JBE -> cf || zf
        | JA -> not cf && not zf
        | JS -> sf
        | JNS -> not sf
        | JP -> pf
        | JNP -> not pf
        | JL -> sf <> ov
        | JGE -> sf = ov
        | JLE -> zf || sf <> ov
        | JG -> not zf && sf = ov

    let private resolveAddress (regs : Registers) (addr : Address) : uint64 =
        match addr with
        | Abs a -> a
        | RipRel d -> regs.RIP + uint64 d
        | BaseDisp (b, i, s, d) ->
            let baseV = Registers.get64 regs b
            let indexV = if i < 0 then 0UL else Registers.get64 regs i
            baseV + indexV * uint64 s + uint64 d

    let private readOp (mem : VirtualMemory) (regs : Registers) (width : int) (operand : Operand) : uint64 =
        match width with
        | 8 ->
            match operand with
            | Reg idx -> uint64 (Registers.get8 regs idx)
            | Imm v -> v &&& 0xFFUL
            | Mem addr -> uint64 (mem.ReadByte (resolveAddress regs addr))
        | 16 ->
            match operand with
            | Reg idx -> Registers.get64 regs idx &&& 0xFFFFUL
            | Imm v -> v &&& 0xFFFFUL
            | Mem addr -> uint64 (mem.ReadUInt16 (resolveAddress regs addr))
        | 32 ->
            match operand with
            | Reg idx -> Registers.get64 regs idx &&& 0xFFFFFFFFUL
            | Imm v -> v &&& 0xFFFFFFFFUL
            | Mem addr -> uint64 (mem.ReadUInt32 (resolveAddress regs addr))
        | _ ->
            match operand with
            | Reg idx -> Registers.get64 regs idx
            | Imm v -> v
            | Mem addr -> mem.ReadUInt64 (resolveAddress regs addr)

    let private writeOp (mem : VirtualMemory) (regs : Registers) (width : int) (operand : Operand) (value : uint64) =
        let mask = maskFor width
        match operand with
        | Reg idx ->
            if width = 8 then Registers.set8 regs idx (byte value)
            elif width = 16 then Registers.set64 regs idx ((Registers.get64 regs idx &&& 0xFFFFFFFFFFFF0000UL) ||| (value &&& 0xFFFFUL))
            elif width = 32 then Registers.set32 regs idx (uint32 value)
            else Registers.set64 regs idx value
        | Mem addr ->
            if width = 8 then mem.WriteByte (resolveAddress regs addr) (byte value)
            elif width = 16 then mem.WriteUInt16 (resolveAddress regs addr) (uint16 value)
            elif width = 32 then mem.WriteUInt32 (resolveAddress regs addr) (uint32 value)
            else mem.WriteUInt64 (resolveAddress regs addr) (value &&& mask)
        | Imm _ -> invalidOp "Écriture dans une constante"

    let private readFull (mem : VirtualMemory) (regs : Registers) (operand : Operand) : uint64 =
        match operand with
        | Reg idx -> Registers.get64 regs idx
        | Imm v -> v
        | Mem addr -> mem.ReadUInt64 (resolveAddress regs addr)

    let private readXmm (mem : VirtualMemory) (regs : Registers) (operand : Operand) : byte[] =
        match operand with
        | Reg idx -> Registers.getXmm regs idx
        | Mem addr -> mem.ReadBytes (resolveAddress regs addr) 16
        | Imm _ -> invalidOp "Opérande immédiate invalide pour une opération XMM"

    let private writeXmm (mem : VirtualMemory) (regs : Registers) (operand : Operand) (data : byte[]) : unit =
        match operand with
        | Reg idx -> Registers.setXmm regs idx data
        | Mem addr -> mem.WriteBytes (resolveAddress regs addr) data
        | Imm _ -> invalidOp "Opérande immédiate invalide pour une opération XMM"

    let private push64 (mem : VirtualMemory) (regs : Registers) (value : uint64) =
        let sp = regs.RSP - 8UL
        mem.WriteUInt64 sp value
        regs.RSP <- sp

    let private pop64 (mem : VirtualMemory) (regs : Registers) : uint64 =
        let value = mem.ReadUInt64 regs.RSP
        regs.RSP <- regs.RSP + 8UL
        value

    /// Répète une itération d'instruction chaîne selon le préfixe rep (0, F2 repne, F3 repe/rep).
    let private repeatExec (regs : Registers) (rep : byte) (checkZf : bool) (doIter : unit -> unit) : unit =
        match rep with
        | 0uy -> doIter ()
        | 0xF2uy ->
            let mutable stop = false
            while (not stop) && regs.RCX <> 0UL do
                doIter ()
                regs.RCX <- regs.RCX - 1UL
                if checkZf && (regs.RFLAGS &&& FlagZf) <> 0UL then stop <- true
        | _ ->
            let mutable stop = false
            while (not stop) && regs.RCX <> 0UL do
                doIter ()
                regs.RCX <- regs.RCX - 1UL
                if checkZf && (regs.RFLAGS &&& FlagZf) = 0UL then stop <- true

    let private execute (host : ISyscallHost) (ins : Instruction) : unit =
        let mem = host.Memory
        let regs = host.Registers
        let nextRip = regs.RIP + uint64 ins.Size
        regs.RIP <- nextRip
        let width = ins.Width
        let mask = maskFor width
        let signBit = signBitFor width

        match ins.Op with
        | Mov (dest, src) ->
            let value = readOp mem regs width src
            writeOp mem regs width dest value

        | Lea (reg, addr) ->
            let value = resolveAddress regs addr
            if width = 32 then Registers.set32 regs reg (uint32 value)
            else Registers.set64 regs reg value

        | Push operand ->
            let value = readFull mem regs operand
            push64 mem regs value

        | Pop operand ->
            let value = pop64 mem regs
            match operand with
            | Reg idx -> Registers.set64 regs idx value
            | _ -> invalidOp "Pop vers une non-registre"

        | Inc operand ->
            let o = readOp mem regs width operand
            let r = (o + 1UL) &&& mask
            let ovf = o = signBit - 1UL
            setFlags regs width r false ovf
            writeOp mem regs width operand r

        | Dec operand ->
            let o = readOp mem regs width operand
            let r = (o - 1UL) &&& mask
            let ovf = o = signBit
            setFlags regs width r false ovf
            writeOp mem regs width operand r

        | Add (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 + o2) &&& mask
            let cf = r < o1
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& signBit <> 0UL
            setFlags regs width r cf ovf
            writeOp mem regs width dest r

        | Sub (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 - o2) &&& mask
            let cf = o1 < o2
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& signBit <> 0UL
            setFlags regs width r cf ovf
            writeOp mem regs width dest r

        | Xor (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 ^^^ o2) &&& mask
            setFlags regs width r false false
            writeOp mem regs width dest r

        | Or (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 ||| o2) &&& mask
            setFlags regs width r false false
            writeOp mem regs width dest r

        | And (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 &&& o2) &&& mask
            setFlags regs width r false false
            writeOp mem regs width dest r

        | Cmp (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 - o2) &&& mask
            let cf = o1 < o2
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& signBit <> 0UL
            setFlags regs width r cf ovf

        | Test (dest, src) ->
            let o1 = readOp mem regs width dest
            let o2 = readOp mem regs width src
            let r = (o1 &&& o2) &&& mask
            setFlags regs width r false false

        | Mov8 (dest, src) ->
            let value = readOp mem regs 8 src
            writeOp mem regs 8 dest value

        | Movzx (dest, src, n) ->
            let value = readOp mem regs n src
            writeOp mem regs width dest value

        | Movsx (dest, src, n) ->
            let v = readOp mem regs n src
            let value = if n = 8 then uint64 (int64 (sbyte v)) else uint64 (int64 (int16 v))
            writeOp mem regs width dest value

        | Movsxd (dest, src) ->
            let v = readOp mem regs 32 src
            if width = 32 then writeOp mem regs 32 dest v
            else writeOp mem regs 64 dest (uint64 (int64 (int32 v)))

        | Xchg (a, b) ->
            let va = readOp mem regs width a
            let vb = readOp mem regs width b
            writeOp mem regs width a vb
            writeOp mem regs width b va

        | Xchg8 (a, b) ->
            let va = readOp mem regs 8 a
            let vb = readOp mem regs 8 b
            writeOp mem regs 8 a vb
            writeOp mem regs 8 b va

        | Cdqe ->
            if width = 64 then
                Registers.set64 regs 0 (uint64 (int64 (int32 (Registers.get32 regs 0))))
            else
                Registers.set32 regs 0 (uint32 (int32 (int16 (uint16 (Registers.get32 regs 0)))))

        | Cqo ->
            if width = 64 then
                let rax = Registers.get64 regs 0
                Registers.set64 regs 2 (if rax &&& 0x8000000000000000UL <> 0UL then 0xFFFFFFFFFFFFFFFFUL else 0UL)
            else
                let eax = Registers.get32 regs 0
                Registers.set32 regs 2 (if eax &&& 0x80000000UL <> 0UL then 0xFFFFFFFFu else 0u)

        | Movaps (dest, src) ->
            writeXmm mem regs dest (readXmm mem regs src)

        | Movups (dest, src) ->
            writeXmm mem regs dest (readXmm mem regs src)

        | Movdqu (dest, src) ->
            writeXmm mem regs dest (readXmm mem regs src)

        | Movdqa (dest, src) ->
            writeXmm mem regs dest (readXmm mem regs src)

        | Pxor (dest, src) ->
            let a = readXmm mem regs dest
            let b = readXmm mem regs src
            writeXmm mem regs dest (Array.init 16 (fun i -> a[i] ^^^ b[i]))

        | Andps (dest, src) ->
            let a = readXmm mem regs dest
            let b = readXmm mem regs src
            writeXmm mem regs dest (Array.init 16 (fun i -> a[i] &&& b[i]))

        | Andnps (dest, src) ->
            let a = readXmm mem regs dest
            let b = readXmm mem regs src
            writeXmm mem regs dest (Array.init 16 (fun i -> ~~~a[i] &&& b[i]))

        | Orps (dest, src) ->
            let a = readXmm mem regs dest
            let b = readXmm mem regs src
            writeXmm mem regs dest (Array.init 16 (fun i -> a[i] ||| b[i]))

        | Xorps (dest, src) ->
            let a = readXmm mem regs dest
            let b = readXmm mem regs src
            writeXmm mem regs dest (Array.init 16 (fun i -> a[i] ^^^ b[i]))

        | Leave ->
            let rbp = Registers.get64 regs 5
            regs.RSP <- rbp
            Registers.set64 regs 5 (pop64 mem regs)

        | Bt (dest, src) ->
            let d = readOp mem regs width dest
            let c = int (readOp mem regs width src &&& (uint64 width - 1UL))
            let bit = (d >>> c) &&& 1UL
            regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagCf) ||| (if bit <> 0UL then FlagCf else 0UL)

        | Bts (dest, src) ->
            let d = readOp mem regs width dest
            let c = int (readOp mem regs width src &&& (uint64 width - 1UL))
            let bit = (d >>> c) &&& 1UL
            regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagCf) ||| (if bit <> 0UL then FlagCf else 0UL)
            writeOp mem regs width dest (d ||| (1UL <<< c))

        | Btr (dest, src) ->
            let d = readOp mem regs width dest
            let c = int (readOp mem regs width src &&& (uint64 width - 1UL))
            let bit = (d >>> c) &&& 1UL
            regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagCf) ||| (if bit <> 0UL then FlagCf else 0UL)
            writeOp mem regs width dest (d &&& ~~~(1UL <<< c))

        | Btc (dest, src) ->
            let d = readOp mem regs width dest
            let c = int (readOp mem regs width src &&& (uint64 width - 1UL))
            let bit = (d >>> c) &&& 1UL
            regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagCf) ||| (if bit <> 0UL then FlagCf else 0UL)
            writeOp mem regs width dest (d ^^^ (1UL <<< c))

        | Bsf (dest, src) ->
            let s = readOp mem regs width src
            if s = 0UL then
                regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagZf) ||| FlagZf
            else
                regs.RFLAGS <- regs.RFLAGS &&& ~~~FlagZf
                let idx =
                    if width = 32 then
                        BitOperations.TrailingZeroCount (uint (s &&& 0xFFFFFFFFUL))
                    else
                        BitOperations.TrailingZeroCount (uint64 s)
                writeOp mem regs width dest (uint64 idx)

        | Bsr (dest, src) ->
            let s = readOp mem regs width src
            if s = 0UL then
                regs.RFLAGS <- (regs.RFLAGS &&& ~~~FlagZf) ||| FlagZf
            else
                regs.RFLAGS <- regs.RFLAGS &&& ~~~FlagZf
                let idx =
                    if width = 32 then
                        31 - BitOperations.LeadingZeroCount (uint (s &&& 0xFFFFFFFFUL))
                    else
                        63 - BitOperations.LeadingZeroCount (uint64 s)
                writeOp mem regs width dest (uint64 idx)

        | Cmpxchg (dest, src) ->
            let d = readOp mem regs width dest
            let a = readOp mem regs width (Reg 0)
            let r = (d - a) &&& mask
            let cf = d < a
            let ovf = ((d ^^^ a) &&& (d ^^^ r)) &&& signBit <> 0UL
            setFlags regs width r cf ovf
            if a = d then
                writeOp mem regs width dest (readOp mem regs width src)
            else
                writeOp mem regs width (Reg 0) d

        | Xadd (dest, src) ->
            let d = readOp mem regs width dest
            let s = readOp mem regs width src
            let r = (d + s) &&& mask
            let cf = r < d
            let ovf = ((d ^^^ s) &&& (d ^^^ r)) &&& signBit <> 0UL
            setFlags regs width r cf ovf
            writeOp mem regs width dest r
            writeOp mem regs width src d

        | Shld (dest, src, count) ->
            let d = readOp mem regs width dest
            let s = readOp mem regs width src
            let bits = if width = 64 then 64 else 32
            let c = count &&& (bits - 1)
            let r = if c = 0 then d else ((d <<< c) ||| (s >>> (bits - c))) &&& mask
            setFlags regs width r (c > 0 && (d >>> (bits - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width dest r

        | Shrd (dest, src, count) ->
            let d = readOp mem regs width dest
            let s = readOp mem regs width src
            let bits = if width = 64 then 64 else 32
            let c = count &&& (bits - 1)
            let r = if c = 0 then d else ((d >>> c) ||| ((s >>> (bits - c)) <<< (bits - c))) &&& mask
            setFlags regs width r (c > 0 && (d >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width dest r

        | ShldCl (dest, src) ->
            let d = readOp mem regs width dest
            let s = readOp mem regs width src
            let bits = if width = 64 then 64 else 32
            let c = int (Registers.get8 regs 1) &&& (bits - 1)
            let r = if c = 0 then d else ((d <<< c) ||| (s >>> (bits - c))) &&& mask
            setFlags regs width r (c > 0 && (d >>> (bits - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width dest r

        | ShrdCl (dest, src) ->
            let d = readOp mem regs width dest
            let s = readOp mem regs width src
            let bits = if width = 64 then 64 else 32
            let c = int (Registers.get8 regs 1) &&& (bits - 1)
            let r = if c = 0 then d else ((d >>> c) ||| ((s >>> (bits - c)) <<< (bits - c))) &&& mask
            setFlags regs width r (c > 0 && (d >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width dest r

        | Add8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 + o2) &&& 0xFFUL
            let cf = r < o1
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& 0x80UL <> 0UL
            setFlags regs 8 r cf ovf
            writeOp mem regs 8 dest r

        | Sub8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 - o2) &&& 0xFFUL
            let cf = o1 < o2
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& 0x80UL <> 0UL
            setFlags regs 8 r cf ovf
            writeOp mem regs 8 dest r

        | And8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 &&& o2) &&& 0xFFUL
            setFlags regs 8 r false false
            writeOp mem regs 8 dest r

        | Or8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 ||| o2) &&& 0xFFUL
            setFlags regs 8 r false false
            writeOp mem regs 8 dest r

        | Xor8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 ^^^ o2) &&& 0xFFUL
            setFlags regs 8 r false false
            writeOp mem regs 8 dest r

        | Cmp8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 - o2) &&& 0xFFUL
            let cf = o1 < o2
            let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& 0x80UL <> 0UL
            setFlags regs 8 r cf ovf

        | Test8 (dest, src) ->
            let o1 = readOp mem regs 8 dest
            let o2 = readOp mem regs 8 src
            let r = (o1 &&& o2) &&& 0xFFUL
            setFlags regs 8 r false false

        | Mul operand ->
            let o = readOp mem regs width operand
            if width = 32 then
                let a = Registers.get32 regs 0
                let p = uint64 a * (o &&& 0xFFFFFFFFUL)
                Registers.set32 regs 0 (uint32 (p &&& 0xFFFFFFFFUL))
                Registers.set32 regs 2 (uint32 (p >>> 32))
                setFlags regs 32 (p &&& 0xFFFFFFFFUL) false (p >>> 32 <> 0UL)
            else
                let p = Math.BigMul (Registers.get64 regs 0, o)
                Registers.set64 regs 0 (uint64 p)
                Registers.set64 regs 2 (uint64 (p >>> 64))
                setFlags regs 64 (uint64 p) false (uint64 (p >>> 64) <> 0UL)

        | Imul operand ->
            let o = readOp mem regs width operand
            if width = 32 then
                let a = int64 (int32 (Registers.get32 regs 0))
                let b = int64 (int32 (o &&& 0xFFFFFFFFUL))
                let p = a * b
                Registers.set32 regs 0 (uint32 p)
                Registers.set32 regs 2 (uint32 (p >>> 32))
                setFlags regs 32 (uint32 p |> uint64) false (p >>> 32 <> (if p < 0L then -1L else 0L))
            else
                let p = Math.BigMul (int64 (Registers.get64 regs 0), int64 o)
                let lo = uint64 p
                let hi = uint64 (p >>> 64)
                Registers.set64 regs 0 lo
                Registers.set64 regs 2 hi
                let signExt = if lo &&& 0x8000000000000000UL <> 0UL then 0xFFFFFFFFFFFFFFFFUL else 0UL
                setFlags regs 64 lo false (hi <> signExt)

        | Div operand ->
            let o = readOp mem regs width operand
            if width = 32 then
                let dividend = ((uint64 (Registers.get32 regs 2)) <<< 32) ||| uint64 (Registers.get32 regs 0)
                let divisor = o &&& 0xFFFFFFFFUL
                if divisor = 0UL then invalidOp "Division par zéro"
                Registers.set32 regs 0 (uint32 (dividend / divisor))
                Registers.set32 regs 2 (uint32 (dividend % divisor))
            else
                if o = 0UL then invalidOp "Division par zéro"
                let dividend = UInt128 (Registers.get64 regs 2, Registers.get64 regs 0)
                let quotient = dividend / UInt128 (0UL, o)
                let remainder = dividend % UInt128 (0UL, o)
                Registers.set64 regs 0 (uint64 quotient)
                Registers.set64 regs 2 (uint64 remainder)

        | Idiv operand ->
            let o = readOp mem regs width operand
            if width = 32 then
                let dividend = (int64 (int32 (Registers.get32 regs 2)) <<< 32) ||| int64 (Registers.get32 regs 0)
                let divisor = int64 (int32 (o &&& 0xFFFFFFFFUL))
                if divisor = 0L then invalidOp "Division par zéro"
                Registers.set32 regs 0 (uint32 (dividend / divisor))
                Registers.set32 regs 2 (uint32 (dividend % divisor))
            else
                if o = 0UL then invalidOp "Division par zéro"
                let dividend = Int128 (Registers.get64 regs 2, Registers.get64 regs 0)
                let divisor = Int128 (0UL, o)
                let quotient = dividend / divisor
                let remainder = dividend % divisor
                Registers.set64 regs 0 (uint64 quotient)
                Registers.set64 regs 2 (uint64 remainder)

        | Not operand ->
            let o = readOp mem regs width operand
            writeOp mem regs width operand ((~~~o) &&& mask)

        | Neg operand ->
            let o = readOp mem regs width operand
            let r = ((~~~o) + 1UL) &&& mask
            let cf = o <> 0UL
            let ovf = o = signBit
            setFlags regs width r cf ovf
            writeOp mem regs width operand r

        | Shl (operand, count) ->
            let o = readOp mem regs width operand
            let c = count &&& 0x3F
            let r = if c = 0 then o else (o <<< c) &&& mask
            setFlags regs width r (c > 0 && (o >>> (64 - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | Shr (operand, count) ->
            let o = readOp mem regs width operand
            let c = count &&& 0x3F
            let r = if c = 0 then o else o >>> c
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | Sar (operand, count) ->
            let o = readOp mem regs width operand
            let c = count &&& 0x3F
            let sign = o &&& signBit
            let r =
                if c = 0 then o
                elif c >= 64 then if sign <> 0UL then ~~~0UL else 0UL
                else (o >>> c) ||| (if sign <> 0UL then ~~~(mask >>> c) else 0UL)
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | Rol (operand, count) ->
            let o = readOp mem regs width operand
            let c = count &&& 0x3F
            let r =
                if c = 0 then o
                else ((o <<< c) ||| (o >>> (64 - c))) &&& mask
            setFlags regs width r (c > 0 && (o >>> (64 - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | Ror (operand, count) ->
            let o = readOp mem regs width operand
            let c = count &&& 0x3F
            let r =
                if c = 0 then o
                else ((o >>> c) ||| (o <<< (64 - c))) &&& mask
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | ShlCl operand ->
            let o = readOp mem regs width operand
            let c = int (Registers.get8 regs 1) &&& 0x3F
            let r = if c = 0 then o else (o <<< c) &&& mask
            setFlags regs width r (c > 0 && (o >>> (64 - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | ShrCl operand ->
            let o = readOp mem regs width operand
            let c = int (Registers.get8 regs 1) &&& 0x3F
            let r = if c = 0 then o else o >>> c
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | SarCl operand ->
            let o = readOp mem regs width operand
            let c = int (Registers.get8 regs 1) &&& 0x3F
            let sign = o &&& signBit
            let r =
                if c = 0 then o
                elif c >= 64 then if sign <> 0UL then ~~~0UL else 0UL
                else (o >>> c) ||| (if sign <> 0UL then ~~~(mask >>> c) else 0UL)
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | RolCl operand ->
            let o = readOp mem regs width operand
            let c = int (Registers.get8 regs 1) &&& 0x3F
            let r =
                if c = 0 then o
                else ((o <<< c) ||| (o >>> (64 - c))) &&& mask
            setFlags regs width r (c > 0 && (o >>> (64 - c)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | RorCl operand ->
            let o = readOp mem regs width operand
            let c = int (Registers.get8 regs 1) &&& 0x3F
            let r =
                if c = 0 then o
                else ((o >>> c) ||| (o <<< (64 - c))) &&& mask
            setFlags regs width r (c > 0 && (o >>> (c - 1)) &&& 1UL <> 0UL) false
            writeOp mem regs width operand r

        | Bswap idx ->
            if width = 64 then
                let v = Registers.get64 regs idx
                let r =
                    (v <<< 56)
                    ||| ((v &&& 0xFF00UL) <<< 40)
                    ||| ((v &&& 0xFF0000UL) <<< 24)
                    ||| ((v &&& 0xFF000000UL) <<< 8)
                    ||| ((v &&& 0xFF00000000UL) >>> 8)
                    ||| ((v &&& 0xFF0000000000UL) >>> 24)
                    ||| ((v &&& 0xFF000000000000UL) >>> 40)
                    ||| (v >>> 56)
                Registers.set64 regs idx r
            else
                let v = uint64 (Registers.get32 regs idx)
                let r =
                    ((v <<< 24) ||| ((v &&& 0xFF00UL) <<< 8) ||| ((v >>> 8) &&& 0xFF00UL) ||| (v >>> 24))
                    &&& 0xFFFFFFFFUL
                Registers.set32 regs idx (uint32 r)

        | Imul2 (dest, src) ->
            let a = readOp mem regs width dest
            let b = readOp mem regs width src
            if width = 32 then
                let p = int64 (int32 (a &&& 0xFFFFFFFFUL)) * int64 (int32 (b &&& 0xFFFFFFFFUL))
                let r = uint64 (uint32 p)
                writeOp mem regs width dest r
                setFlags regs width r false (p >>> 32 <> (if p < 0L then -1L else 0L))
            else
                let p = Math.BigMul (int64 a, int64 b)
                let lo = uint64 p
                let signExt = if lo &&& 0x8000000000000000UL <> 0UL then 0xFFFFFFFFFFFFFFFFUL else 0UL
                writeOp mem regs width dest lo
                setFlags regs width lo false (uint64 (p >>> 64) <> signExt)

        | Call target ->
            push64 mem regs nextRip
            regs.RIP <- nextRip + uint64 target

        | CallInd operand ->
            let target = readFull mem regs operand
            push64 mem regs nextRip
            regs.RIP <- target

        | Jmp target ->
            regs.RIP <- nextRip + uint64 target

        | JmpInd operand ->
            regs.RIP <- readFull mem regs operand

        | Jcc (target, cond) ->
            if condHolds regs cond then
                regs.RIP <- nextRip + uint64 target

        | Syscall ->
            Syscalls.dispatch host

        | Ret ->
            regs.RIP <- pop64 mem regs

        | Nop -> ()

        | Hlt ->
            host.Halted <- true
            host.ExitStatus <- 0

        | Out port ->
            let p =
                match port with
                | Reg idx -> Registers.get64 regs idx
                | Imm v -> v
                | Mem _ -> invalidOp "Port mémoire non pris en charge"
            if p = 0x3F8UL then
                host.StandardOutput.WriteByte (byte (Registers.get64 regs 0))

        | In port ->
            let p =
                match port with
                | Reg idx -> Registers.get64 regs idx
                | Imm v -> v
                | Mem _ -> invalidOp "Port mémoire non pris en charge"
            let v = if p = 0x3FDUL then 0x60uy else 0xFFuy
            let rax = Registers.get64 regs 0
            Registers.set64 regs 0 ((rax &&& 0xFFFF_FFFF_FFFF_FF00UL) ||| uint64 v)

        | Cld ->
            regs.RFLAGS <- regs.RFLAGS &&& ~~~FlagDf

        | Std ->
            regs.RFLAGS <- regs.RFLAGS ||| FlagDf

        | Setcc (cond, dest) ->
            let v = if condHolds regs cond then 1UL else 0UL
            match dest with
            | Reg idx -> Registers.set64 regs idx v
            | Mem addr -> mem.WriteByte (resolveAddress regs addr) (byte v)
            | Imm _ -> invalidOp "Écriture dans une constante"

        | Cmov (cond, dest, src) ->
            if condHolds regs cond then
                writeOp mem regs ins.Width dest (readOp mem regs ins.Width src)

        | Movs w ->
            let step = uint64 w
            let doIter () =
                let v = readOp mem regs w (Mem (Abs regs.RSI))
                writeOp mem regs w (Mem (Abs regs.RDI)) v
                if regs.RFLAGS &&& FlagDf <> 0UL then
                    regs.RSI <- regs.RSI - step
                    regs.RDI <- regs.RDI - step
                else
                    regs.RSI <- regs.RSI + step
                    regs.RDI <- regs.RDI + step
            repeatExec regs ins.Rep false doIter

        | Stos w ->
            let step = uint64 w
            let doIter () =
                let v = readOp mem regs w (Reg 0)
                writeOp mem regs w (Mem (Abs regs.RDI)) v
                if regs.RFLAGS &&& FlagDf <> 0UL then regs.RDI <- regs.RDI - step
                else regs.RDI <- regs.RDI + step
            repeatExec regs ins.Rep false doIter

        | Lods w ->
            let step = uint64 w
            let doIter () =
                let v = readOp mem regs w (Mem (Abs regs.RSI))
                writeOp mem regs w (Reg 0) v
                if regs.RFLAGS &&& FlagDf <> 0UL then regs.RSI <- regs.RSI - step
                else regs.RSI <- regs.RSI + step
            repeatExec regs ins.Rep false doIter

        | Scas w ->
            let step = uint64 w
            let doIter () =
                let m = maskFor w
                let sb = signBitFor w
                let o1 = readOp mem regs w (Mem (Abs regs.RDI))
                let o2 = readOp mem regs w (Reg 0)
                let r = (o1 - o2) &&& m
                let cf = o1 < o2
                let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& sb <> 0UL
                setFlags regs w r cf ovf
                if regs.RFLAGS &&& FlagDf <> 0UL then regs.RDI <- regs.RDI - step
                else regs.RDI <- regs.RDI + step
            repeatExec regs ins.Rep true doIter

        | Cmps w ->
            let step = uint64 w
            let doIter () =
                let m = maskFor w
                let sb = signBitFor w
                let o1 = readOp mem regs w (Mem (Abs regs.RSI))
                let o2 = readOp mem regs w (Mem (Abs regs.RDI))
                let r = (o1 - o2) &&& m
                let cf = o1 < o2
                let ovf = ((o1 ^^^ o2) &&& (o1 ^^^ r)) &&& sb <> 0UL
                setFlags regs w r cf ovf
                if regs.RFLAGS &&& FlagDf <> 0UL then
                    regs.RSI <- regs.RSI - step
                    regs.RDI <- regs.RDI - step
                else
                    regs.RSI <- regs.RSI + step
                    regs.RDI <- regs.RDI + step
            repeatExec regs ins.Rep true doIter

        | Unknown ->
            invalidOp $"Instruction non supportée à l'adresse 0x{nextRip - uint64 ins.Size:X} (taille {ins.Size})"

    /// Exécute une seule instruction (avec commutation de tâche si nécessaire).
    let private stepCore (host : ISyscallHost) (onSyscall : (uint64 -> unit) option) : unit =
        let mem = host.Memory
        let regs = host.Registers
        if host.NeedsSwitch then
            host.NeedsSwitch <- false
            host.Restore host.TaskQueue.[0]
            host.TaskQueue.RemoveAt 0
        if not host.Halted then
            let ins = Decoder.decode mem regs.RIP
            match onSyscall with
            | Some f when ins.Op = Syscall -> f regs.RAX
            | _ -> ()
            execute host ins

    /// Exécute le programme jusqu'à l'arrêt.
    let run (host : ISyscallHost) (onSyscall : (uint64 -> unit) option) : unit =
        while not host.Halted do
            stepCore host onSyscall

    /// Exécute une seule instruction ; retourne false si la machine est arrêtée.
    let step (host : ISyscallHost) (onSyscall : (uint64 -> unit) option) : bool =
        stepCore host onSyscall
        not host.Halted
