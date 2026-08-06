namespace Diplo.Linux

/// Mode d'adressage mémoire.
type Address =
    | Abs of uint64
    | RipRel of int64
    | BaseDisp of baseReg : int * indexReg : int * scale : int * disp : int64

/// Opérande d'une instruction.
type Operand =
    | Reg of int
    | Imm of uint64
    | Mem of Address

/// Condition d'un saut conditionnel.
type FlagCond =
    | JO
    | JNO
    | JB
    | JAE
    | JE
    | JNE
    | JBE
    | JA
    | JS
    | JNS
    | JP
    | JNP
    | JL
    | JGE
    | JLE
    | JG

/// Opération d'une instruction décodée.
type Op =
    | Mov of Operand * Operand
    | Mov8 of Operand * Operand
    | Movzx of Operand * Operand * int
    | Movsx of Operand * Operand * int
    | Lea of int * Address
    | Push of Operand
    | Pop of Operand
    | Inc of Operand
    | Dec of Operand
    | Add of Operand * Operand
    | Add8 of Operand * Operand
    | Sub of Operand * Operand
    | Sub8 of Operand * Operand
    | Xor of Operand * Operand
    | Xor8 of Operand * Operand
    | Or of Operand * Operand
    | Or8 of Operand * Operand
    | And of Operand * Operand
    | And8 of Operand * Operand
    | Cmp of Operand * Operand
    | Cmp8 of Operand * Operand
    | Test of Operand * Operand
    | Test8 of Operand * Operand
    | Shl of Operand * int
    | Shr of Operand * int
    | Sar of Operand * int
    | Rol of Operand * int
    | Ror of Operand * int
    | ShlCl of Operand
    | ShrCl of Operand
    | SarCl of Operand
    | RolCl of Operand
    | RorCl of Operand
    | Mul of Operand
    | Imul of Operand
    | Imul2 of Operand * Operand
    | Bswap of int
    | Div of Operand
    | Idiv of Operand
    | Not of Operand
    | Neg of Operand
    | Movsxd of Operand * Operand
    | Xchg of Operand * Operand
    | Xchg8 of Operand * Operand
    | Cdqe
    | Cqo
    | Movaps of Operand * Operand
    | Movups of Operand * Operand
    | Movdqu of Operand * Operand
    | Movdqa of Operand * Operand
    | Pxor of Operand * Operand
    | Andps of Operand * Operand
    | Andnps of Operand * Operand
    | Orps of Operand * Operand
    | Xorps of Operand * Operand
    | Leave
    | Bt of Operand * Operand
    | Bts of Operand * Operand
    | Btr of Operand * Operand
    | Btc of Operand * Operand
    | Bsf of Operand * Operand
    | Bsr of Operand * Operand
    | Cmpxchg of Operand * Operand
    | Xadd of Operand * Operand
    | Shld of Operand * Operand * int
    | Shrd of Operand * Operand * int
    | ShldCl of Operand * Operand
    | ShrdCl of Operand * Operand
    | Call of uint64
    | CallInd of Operand
    | Jmp of uint64
    | JmpInd of Operand
    | Jcc of uint64 * FlagCond
    | Movs of int
    | Stos of int
    | Lods of int
    | Scas of int
    | Cmps of int
    | Cld
    | Std
    | Setcc of FlagCond * Operand
    | Cmov of FlagCond * Operand * Operand
    | Syscall
    | Ret
    | Nop
    | Hlt
    | Out of Operand
    | In of Operand
    | Unknown

/// Instruction décodée.
type Instruction =
    { Op : Op
      Size : int
      Width : int
      Rep : byte }

/// Décodeur d'instructions x86-64 (sous-ensemble).
module Decoder =

    let private invalidOp (msg : string) : 'a = failwith msg

    let private isRex (b : byte) = b >= 0x40uy && b <= 0x4Fuy

    /// Décode une instruction à l'adresse donnée.
    let decode (mem : VirtualMemory) (rip : uint64) : Instruction =
        let ip = ref rip
        let mutable rex = 0uy
        let mutable opSize16 = false
        let mutable rep = 0uy

        let mutable prefixDone = false
        let mutable opcode = 0uy
        while not prefixDone do
            let b = mem.ReadByte !ip
            if isRex b then
                rex <- b
                ip.Value <- ip.Value + 1UL
            else if b = 0x66uy then
                opSize16 <- true
                ip.Value <- ip.Value + 1UL
            else if b = 0xF2uy || b = 0xF3uy then
                rep <- b
                ip.Value <- ip.Value + 1UL
            else if b = 0x67uy || b = 0x2Euy || b = 0x36uy || b = 0x3Euy
                    || b = 0x26uy || b = 0x64uy || b = 0x65uy || b = 0xF0uy then
                ip.Value <- ip.Value + 1UL
            else
                opcode <- b
                ip.Value <- ip.Value + 1UL
                prefixDone <- true

        let rexW = rex &&& 0x08uy <> 0uy
        let rexB = rex &&& 0x01uy <> 0uy
        let rexX = rex &&& 0x02uy <> 0uy
        let rexR = rex &&& 0x04uy <> 0uy

        let nextByte () =
            let b = mem.ReadByte !ip
            ip.Value <- ip.Value + 1UL
            b

        let next16 () =
            let v = mem.ReadUInt16 !ip
            ip.Value <- ip.Value + 2UL
            v

        let next32 () =
            let v = mem.ReadUInt32 !ip
            ip.Value <- ip.Value + 4UL
            v

        let next64 () =
            let v = mem.ReadUInt64 !ip
            ip.Value <- ip.Value + 8UL
            v

        let nextS8 () = int64 (int8 (nextByte ()))
        let nextS32 () = int64 (int32 (next32 ()))

        let modRM () =
            let b = nextByte ()
            (int (b >>> 6), int (b >>> 3) &&& 7, int b &&& 7)

        let readDisp (m : int) (rm : int) =
            if m = 0 then 0L
            else if m = 1 then nextS8 ()
            else nextS32 ()

        let memAddress (m : int) (rm : int) : Address =
            if m = 3 then
                invalidOp "ModRM mod=3 attendu pour une opérande mémoire"
            else if rm = 4 then
                let sib = nextByte ()
                let scale = 1 <<< (int (sib >>> 6))
                let indexField = (int (sib >>> 3) &&& 7) ||| (if rexX then 8 else 0)
                let hasIndex = not ((int (sib >>> 3) &&& 7) = 4 && not rexX)
                let index = if hasIndex then indexField else -1
                let baseReg = (int sib &&& 7) ||| (if rexB then 8 else 0)
                if m = 0 && baseReg = 5 then
                    Abs (uint64 (next32 ()))
                else
                    let disp = readDisp m baseReg
                    BaseDisp (baseReg, index, scale, disp)
            else if rm = 5 && m = 0 then
                RipRel (nextS32 ())
            else
                let baseReg = rm ||| (if rexB then 8 else 0)
                let disp = readDisp m baseReg
                BaseDisp (baseReg, -1, 1, disp)

        let memOperand (m : int) (rm : int) = Mem (memAddress m rm)

        /// Opérande r/m : registre si mod=3, sinon adresse mémoire.
        let rmOperand (m : int) (rm : int) =
            if m = 3 then Reg (rm ||| (if rexB then 8 else 0))
            else Mem (memAddress m rm)

        let regOperand (reg : int) = Reg (reg ||| (if rexR then 8 else 0))

        let width = if rexW then 64 elif opSize16 then 16 else 32

        let mk (op : Op) (width : int) : Instruction =
            { Op = op; Size = int (!ip - rip); Width = width; Rep = rep }

        let immOp (useImm : bool) (m : int) (reg : int) (rm : int) : Op =
            if useImm then
                let dest = rmOperand m rm
                let value =
                    if width = 16 then uint64 (next16 ())
                    else uint64 (int64 (int32 (next32 ())))
                Mov (dest, Imm value)
            else
                invalidOp "C7 attendu avec /0"

        let group81 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            let value =
                if width = 16 then uint64 (next16 ())
                else uint64 (int64 (int32 (next32 ())))
            match reg with
            | 0 -> Add (dest, Imm value)
            | 1 -> Or (dest, Imm value)
            | 4 -> And (dest, Imm value)
            | 5 -> Sub (dest, Imm value)
            | 6 -> Xor (dest, Imm value)
            | 7 -> Cmp (dest, Imm value)
            | _ -> invalidOp $"Groupe 81 avec reg={reg} non pris en charge"

        let group83 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            let value = nextS8 ()
            match reg with
            | 0 -> Add (dest, Imm (uint64 value))
            | 1 -> Or (dest, Imm (uint64 value))
            | 4 -> And (dest, Imm (uint64 value))
            | 5 -> Sub (dest, Imm (uint64 value))
            | 6 -> Xor (dest, Imm (uint64 value))
            | 7 -> Cmp (dest, Imm (uint64 value))
            | _ -> invalidOp $"Groupe 83 avec reg={reg} non pris en charge"

        let groupC1 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            let count = int (nextByte ())
            match reg with
            | 0 -> Rol (dest, count)
            | 1 -> Ror (dest, count)
            | 4 -> Shl (dest, count)
            | 5 -> Shr (dest, count)
            | 6 -> Shl (dest, count)
            | 7 -> Sar (dest, count)
            | _ -> invalidOp $"Groupe C1 avec reg={reg} non pris en charge"

        let groupD1 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            match reg with
            | 0 -> Rol (dest, 1)
            | 1 -> Ror (dest, 1)
            | 4 -> Shl (dest, 1)
            | 5 -> Shr (dest, 1)
            | 6 -> Shl (dest, 1)
            | 7 -> Sar (dest, 1)
            | _ -> invalidOp $"Groupe D1 avec reg={reg} non pris en charge"

        let groupD3 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            match reg with
            | 0 -> RolCl dest
            | 1 -> RorCl dest
            | 4 -> ShlCl dest
            | 5 -> ShrCl dest
            | 6 -> ShlCl dest
            | 7 -> SarCl dest
            | _ -> invalidOp $"Groupe D3 avec reg={reg} non pris en charge"

        let groupFF (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            match reg with
            | 0 -> Inc dest
            | 1 -> Dec dest
            | 2 -> CallInd dest
            | 4 -> JmpInd dest
            | 6 -> Push dest
            | _ -> invalidOp $"Groupe FF avec reg={reg} non pris en charge"

        let group80 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            let value = uint64 (nextByte ())
            match reg with
            | 0 -> Add8 (dest, Imm value)
            | 1 -> Or8 (dest, Imm value)
            | 4 -> And8 (dest, Imm value)
            | 5 -> Sub8 (dest, Imm value)
            | 6 -> Xor8 (dest, Imm value)
            | 7 -> Cmp8 (dest, Imm value)
            | _ -> invalidOp $"Groupe 80 avec reg={reg} non pris en charge"

        let groupF7 (reg : int) (m : int) (rm : int) : Op =
            let dest = rmOperand m rm
            match reg with
            | 0 -> Test (dest, Imm (uint64 (int64 (int32 (next32 ())))))
            | 2 -> Not dest
            | 3 -> Neg dest
            | 4 -> Mul dest
            | 5 -> Imul dest
            | 6 -> Div dest
            | 7 -> Idiv dest
            | _ -> invalidOp $"Groupe F7 avec reg={reg} non pris en charge"

        let jccOp (cond : FlagCond) =
            Jcc (uint64 (next32 ()), cond)

        let condOfRel (second : byte) =
            match second with
            | 0x84uy -> JE
            | 0x85uy -> JNE
            | 0x86uy -> JBE
            | 0x87uy -> JA
            | 0x88uy -> JS
            | 0x89uy -> JNS
            | 0x8Auy -> JP
            | 0x8Buy -> JNP
            | 0x8Cuy -> JL
            | 0x8Duy -> JGE
            | 0x8Euy -> JLE
            | _ -> JG

        let condOfIndex (k : byte) =
            match k with
            | 0uy -> JO
            | 1uy -> JNO
            | 2uy -> JB
            | 3uy -> JAE
            | 4uy -> JE
            | 5uy -> JNE
            | 6uy -> JBE
            | 7uy -> JA
            | 8uy -> JS
            | 9uy -> JNS
            | 10uy -> JP
            | 11uy -> JNP
            | 12uy -> JL
            | 13uy -> JGE
            | 14uy -> JLE
            | _ -> JG

        let op : Op =
            if opcode = 0x0Fuy then
                let second = nextByte ()
                if second = 0x05uy then Syscall
                else if second = 0x1Fuy then
                    let (m, _, rm) = modRM ()
                    if m <> 3 then memAddress m rm |> ignore
                    Nop
                else if second >= 0x40uy && second <= 0x4Fuy then
                    let (m, reg, rm) = modRM ()
                    Cmov (condOfIndex (second - 0x40uy), regOperand reg, rmOperand m rm)
                else if second >= 0x90uy && second <= 0x9Fuy then
                    let (m, _, rm) = modRM ()
                    Setcc (condOfIndex (second - 0x90uy), rmOperand m rm)
                else if second = 0xB6uy then
                    let (m, reg, rm) = modRM ()
                    Movzx (regOperand reg, rmOperand m rm, 8)
                else if second = 0xB7uy then
                    let (m, reg, rm) = modRM ()
                    Movzx (regOperand reg, rmOperand m rm, 16)
                else if second = 0xBEuy then
                    let (m, reg, rm) = modRM ()
                    Movsx (regOperand reg, rmOperand m rm, 8)
                else if second = 0xBFuy then
                    let (m, reg, rm) = modRM ()
                    Movsx (regOperand reg, rmOperand m rm, 16)
                else if second = 0x28uy then
                    let (m, reg, rm) = modRM ()
                    Movaps (regOperand reg, rmOperand m rm)
                else if second = 0x29uy then
                    let (m, reg, rm) = modRM ()
                    Movaps (rmOperand m rm, regOperand reg)
                else if second = 0xAFuy then
                    let (m, reg, rm) = modRM ()
                    Imul2 (regOperand reg, rmOperand m rm)
                else if second >= 0xC8uy && second <= 0xCFuy then
                    Bswap ((int second - 0xC8) ||| (if rexB then 8 else 0))
                else if second >= 0x84uy && second <= 0x8Fuy then
                    jccOp (condOfRel second)
                else if second = 0x10uy then
                    let (m, reg, rm) = modRM ()
                    Movups (regOperand reg, rmOperand m rm)
                else if second = 0x11uy then
                    let (m, reg, rm) = modRM ()
                    Movups (rmOperand m rm, regOperand reg)
                else if second = 0x54uy then
                    let (m, reg, rm) = modRM ()
                    Andps (regOperand reg, rmOperand m rm)
                else if second = 0x55uy then
                    let (m, reg, rm) = modRM ()
                    Andnps (regOperand reg, rmOperand m rm)
                else if second = 0x56uy then
                    let (m, reg, rm) = modRM ()
                    Orps (regOperand reg, rmOperand m rm)
                else if second = 0x57uy then
                    let (m, reg, rm) = modRM ()
                    Xorps (regOperand reg, rmOperand m rm)
                else if second = 0x6Fuy then
                    let (m, reg, rm) = modRM ()
                    if opSize16 then Movdqa (regOperand reg, rmOperand m rm)
                    else Movdqu (regOperand reg, rmOperand m rm)
                else if second = 0x7Fuy then
                    let (m, reg, rm) = modRM ()
                    if opSize16 then Movdqa (rmOperand m rm, regOperand reg)
                    else Movdqu (rmOperand m rm, regOperand reg)
                else if second = 0xEFuy then
                    let (m, reg, rm) = modRM ()
                    Pxor (regOperand reg, rmOperand m rm)
                else if second = 0xA3uy then
                    let (m, reg, rm) = modRM ()
                    Bt (rmOperand m rm, regOperand reg)
                else if second = 0xABuy then
                    let (m, reg, rm) = modRM ()
                    Bts (rmOperand m rm, regOperand reg)
                else if second = 0xB3uy then
                    let (m, reg, rm) = modRM ()
                    Btr (rmOperand m rm, regOperand reg)
                else if second = 0xBBuy then
                    let (m, reg, rm) = modRM ()
                    Btc (rmOperand m rm, regOperand reg)
                else if second = 0xBCuy then
                    let (m, reg, rm) = modRM ()
                    Bsf (regOperand reg, rmOperand m rm)
                else if second = 0xBDuy then
                    let (m, reg, rm) = modRM ()
                    Bsr (regOperand reg, rmOperand m rm)
                else if second = 0xB1uy then
                    let (m, reg, rm) = modRM ()
                    Cmpxchg (rmOperand m rm, regOperand reg)
                else if second = 0xC1uy then
                    let (m, reg, rm) = modRM ()
                    Xadd (rmOperand m rm, regOperand reg)
                else if second = 0xA4uy then
                    let (m, reg, rm) = modRM ()
                    Shld (rmOperand m rm, regOperand reg, int (nextByte ()))
                else if second = 0xA5uy then
                    let (m, reg, rm) = modRM ()
                    ShldCl (rmOperand m rm, regOperand reg)
                else if second = 0xACuy then
                    let (m, reg, rm) = modRM ()
                    Shrd (rmOperand m rm, regOperand reg, int (nextByte ()))
                else if second = 0xADuy then
                    let (m, reg, rm) = modRM ()
                    ShrdCl (rmOperand m rm, regOperand reg)
                else if second = 0xBAuy then
                    let (m, reg, rm) = modRM ()
                    let dest = rmOperand m rm
                    let count = int (nextByte ())
                    match reg with
                    | 4 -> Bt (dest, Imm (uint64 count))
                    | 5 -> Bts (dest, Imm (uint64 count))
                    | 6 -> Btr (dest, Imm (uint64 count))
                    | 7 -> Btc (dest, Imm (uint64 count))
                    | _ -> invalidOp $"Groupe BA avec reg={reg} non pris en charge"
                else invalidOp $"Opcode 0F {second:X2} non pris en charge"
            else if opcode = 0x70uy then Jcc (uint64 (nextS8 ()), JO)
            else if opcode = 0x71uy then Jcc (uint64 (nextS8 ()), JNO)
            else if opcode = 0x72uy then Jcc (uint64 (nextS8 ()), JB)
            else if opcode = 0x73uy then Jcc (uint64 (nextS8 ()), JAE)
            else if opcode = 0x74uy then Jcc (uint64 (nextS8 ()), JE)
            else if opcode = 0x75uy then Jcc (uint64 (nextS8 ()), JNE)
            else if opcode = 0x76uy then Jcc (uint64 (nextS8 ()), JBE)
            else if opcode = 0x77uy then Jcc (uint64 (nextS8 ()), JA)
            else if opcode = 0x78uy then Jcc (uint64 (nextS8 ()), JS)
            else if opcode = 0x79uy then Jcc (uint64 (nextS8 ()), JNS)
            else if opcode = 0x7Auy then Jcc (uint64 (nextS8 ()), JP)
            else if opcode = 0x7Buy then Jcc (uint64 (nextS8 ()), JNP)
            else if opcode = 0x7Cuy then Jcc (uint64 (nextS8 ()), JL)
            else if opcode = 0x7Duy then Jcc (uint64 (nextS8 ()), JGE)
            else if opcode = 0x7Euy then Jcc (uint64 (nextS8 ()), JLE)
            else if opcode = 0x7Fuy then Jcc (uint64 (nextS8 ()), JG)
            else if opcode = 0x68uy then Push (Imm (uint64 (int64 (int32 (next32 ())))))
            else if opcode = 0x6Auy then Push (Imm (uint64 (nextS8 ())))
            else if opcode >= 0x90uy && opcode <= 0x97uy then
                if rexW then
                    Xchg (Reg 0, Reg ((int opcode - 0x90) ||| (if rexB then 8 else 0)))
                elif opcode = 0x90uy then Nop
                else invalidOp "Xchg sans REX.W non pris en charge"
            else if opcode = 0xF4uy then Hlt
            else if opcode = 0xEEuy then Out (Reg 2)
            else if opcode = 0xE6uy then Out (Imm (uint64 (nextByte ())))
            else if opcode = 0xECuy then In (Reg 2)
            else if opcode = 0xE4uy then In (Imm (uint64 (nextByte ())))
            else if opcode = 0xC3uy then Ret
            else if opcode = 0xC9uy then Leave
            else if opcode = 0xC2uy then
                next16 () |> ignore
                Ret
            else if opcode = 0xE8uy then Call (uint64 (nextS32 ()))
            else if opcode = 0xE9uy then Jmp (uint64 (nextS32 ()))
            else if opcode = 0xEBuy then Jmp (uint64 (nextS8 ()))
            else if opcode >= 0x50uy && opcode <= 0x57uy then Push (Reg ((int opcode - 0x50) ||| (if rexB then 8 else 0)))
            else if opcode >= 0x58uy && opcode <= 0x5Fuy then Pop (Reg ((int opcode - 0x58) ||| (if rexB then 8 else 0)))
            else if opcode >= 0xB8uy && opcode <= 0xBFuy then
                let reg = (int opcode - 0xB8) ||| (if rexB then 8 else 0)
                let value =
                    if rexW then next64 ()
                    elif opSize16 then uint64 (next16 ())
                    else uint64 (next32 ())
                Mov (Reg reg, Imm value)
            else if opcode = 0x89uy then
                let (m, reg, rm) = modRM ()
                Mov (rmOperand m rm, regOperand reg)
            else if opcode = 0x8Buy then
                let (m, reg, rm) = modRM ()
                Mov (regOperand reg, rmOperand m rm)
            else if opcode = 0x8Duy then
                let (m, reg, rm) = modRM ()
                Lea (reg ||| (if rexR then 8 else 0), memAddress m rm)
            else if opcode = 0x63uy then
                let (m, reg, rm) = modRM ()
                Movsxd (regOperand reg, rmOperand m rm)
            else if opcode = 0x86uy then
                let (m, reg, rm) = modRM ()
                Xchg8 (rmOperand m rm, regOperand reg)
            else if opcode = 0x87uy then
                let (m, reg, rm) = modRM ()
                Xchg (rmOperand m rm, regOperand reg)
            else if opcode = 0x98uy then Cdqe
            else if opcode = 0x99uy then Cqo
            else if opcode = 0x31uy then
                let (m, reg, rm) = modRM ()
                Xor (rmOperand m rm, regOperand reg)
            else if opcode = 0x01uy then
                let (m, reg, rm) = modRM ()
                Add (rmOperand m rm, regOperand reg)
            else if opcode = 0x03uy then
                let (m, reg, rm) = modRM ()
                Add (regOperand reg, rmOperand m rm)
            else if opcode = 0x23uy then
                let (m, reg, rm) = modRM ()
                And (regOperand reg, rmOperand m rm)
            else if opcode = 0x2Buy then
                let (m, reg, rm) = modRM ()
                Sub (regOperand reg, rmOperand m rm)
            else if opcode = 0x33uy then
                let (m, reg, rm) = modRM ()
                Xor (regOperand reg, rmOperand m rm)
            else if opcode = 0x3Buy then
                let (m, reg, rm) = modRM ()
                Cmp (regOperand reg, rmOperand m rm)
            else if opcode = 0x29uy then
                let (m, reg, rm) = modRM ()
                Sub (rmOperand m rm, regOperand reg)
            else if opcode = 0x21uy then
                let (m, reg, rm) = modRM ()
                And (rmOperand m rm, regOperand reg)
            else if opcode = 0x09uy then
                let (m, reg, rm) = modRM ()
                Or (rmOperand m rm, regOperand reg)
            else if opcode = 0x0Buy then
                let (m, reg, rm) = modRM ()
                Or (regOperand reg, rmOperand m rm)
            else if opcode = 0x08uy then
                let (m, reg, rm) = modRM ()
                Or8 (rmOperand m rm, regOperand reg)
            else if opcode = 0x0Auy then
                let (m, reg, rm) = modRM ()
                Or8 (regOperand reg, rmOperand m rm)
            else if opcode = 0x04uy then Add8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x0Cuy then Or8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x24uy then And8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x2Cuy then Sub8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x34uy then Xor8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x3Cuy then Cmp8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0x05uy then Add (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x0Duy then Or (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x25uy then And (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x2Duy then Sub (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x35uy then Xor (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x3Duy then Cmp (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode = 0x39uy then
                let (m, reg, rm) = modRM ()
                Cmp (rmOperand m rm, regOperand reg)
            else if opcode = 0x85uy then
                let (m, reg, rm) = modRM ()
                Test (rmOperand m rm, regOperand reg)
            else if opcode = 0xA8uy then Test8 (Reg 0, Imm (uint64 (nextByte ())))
            else if opcode = 0xA9uy then
                Test (Reg 0, Imm (if rexW then uint64 (nextS32 ()) elif opSize16 then uint64 (next16 ()) else uint64 (nextS32 ())))
            else if opcode >= 0xB0uy && opcode <= 0xB7uy then
                Mov8 (Reg ((int opcode - 0xB0) ||| (if rexB then 8 else 0)), Imm (uint64 (nextByte ())))
            else if opcode = 0x80uy then
                let (m, reg, rm) = modRM ()
                group80 reg m rm
            else if opcode = 0x81uy then
                let (m, reg, rm) = modRM ()
                group81 reg m rm
            else if opcode = 0x83uy then
                let (m, reg, rm) = modRM ()
                group83 reg m rm
            else if opcode = 0x88uy then
                let (m, reg, rm) = modRM ()
                Mov8 (rmOperand m rm, regOperand reg)
            else if opcode = 0x8Auy then
                let (m, reg, rm) = modRM ()
                Mov8 (regOperand reg, rmOperand m rm)
            else if opcode = 0xC6uy then
                let (m, reg, rm) = modRM ()
                if reg <> 0 then invalidOp "C6 attendu avec /0"
                Mov8 (rmOperand m rm, Imm (uint64 (nextByte ())))
            else if opcode = 0xC7uy then
                let (m, reg, rm) = modRM ()
                immOp (reg = 0) m reg rm
            else if opcode = 0xC1uy then
                let (m, reg, rm) = modRM ()
                groupC1 reg m rm
            else if opcode = 0xD1uy then
                let (m, reg, rm) = modRM ()
                groupD1 reg m rm
            else if opcode = 0xD3uy then
                let (m, reg, rm) = modRM ()
                groupD3 reg m rm
            else if opcode = 0xF7uy then
                let (m, reg, rm) = modRM ()
                groupF7 reg m rm
            else if opcode = 0xFFuy then
                let (m, reg, rm) = modRM ()
                groupFF reg m rm
            else if opcode = 0xA4uy then Movs 1
            else if opcode = 0xA5uy then Movs width
            else if opcode = 0xA6uy then Cmps 1
            else if opcode = 0xA7uy then Cmps width
            else if opcode = 0xAAuy then Stos 1
            else if opcode = 0xABuy then Stos width
            else if opcode = 0xACuy then Lods 1
            else if opcode = 0xADuy then Lods width
            else if opcode = 0xAEuy then Scas 1
            else if opcode = 0xAFuy then Scas width
            else if opcode = 0xFCuy then Cld
            else if opcode = 0xFDuy then Std
            else
                invalidOp $"Opcode {opcode:X2} non pris en charge à RIP 0x{rip:X}"

        mk op width

    let private flagCondFromRel (cond : FlagCond) =
        cond

    /// Retourne la taille décodée d'une instruction.
    let size (ins : Instruction) = ins.Size
