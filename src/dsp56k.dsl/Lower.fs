/// Optional passes over DSL programs: folding single moves into the parallel slots of the
/// neighbouring instruction, and the `nop` the address generation pipeline needs after an
/// address register is loaded. Programs written to match an existing listing word for word
/// simply skip these.
module dsp56k.dsl.Lower
open dsp56k.dsl.Asm

open dsp56k

/// The storage a register name touches (a1 and a overlap; x is x1 and x0).
let storage (r: Reg) =
  match r with
  | A | A0 | A1 | A2 | A10 -> [| A |]
  | B | B0 | B1 | B2 | B10 -> [| B |]
  | AB | BA -> [| A; B |]
  | X -> [| X0; X1 |]
  | Y -> [| Y0; Y1 |]
  | r -> [| r |]

let eaRegs ea =
  match ea with
  | PostDecN r | PostIncN r | IndexN r -> [| R r; N r |]
  | PostDec r | PostInc r | Ind r | PreDec r | Disp(r, _) -> [| R r |]
  | Abs _ -> [||]

let eaWrites ea =
  match ea with
  | PostDecN r | PostIncN r | PostDec r | PostInc r | PreDec r -> [| R r |]
  | _ -> [||]

let moveReads m =
  match m with
  | Load(_, ea, _) -> eaRegs ea
  | Store(r, _, ea) -> Array.append (storage r) (eaRegs ea)
  | Copy(s, _) -> storage s
  | Imm _ -> [||]
  | Update ea -> eaRegs ea

let moveWrites m =
  match m with
  | Load(_, ea, r) -> Array.append (storage r) (eaWrites ea)
  | Store(_, _, ea) | Update ea -> eaWrites ea
  | Copy(_, d) | Imm(_, d) -> storage d

let aluReads a =
  match a with
  | Binary(AluOp.Tfr, s, _) -> storage s
  | Binary(_, s, d) -> Array.append (storage s) [| Acc.reg d |]
  | Unary(AluUnary.Clr, _) -> [||]
  | Unary(_, d) -> [| Acc.reg d |]
  | Mul(kind, _, s1, s2, d) ->
    match kind with
    | Mac | Macr -> [| s1; s2; Acc.reg d |]
    | _ -> [| s1; s2 |]
  | Max | Maxm -> [| A; B |]

let aluWrites a =
  match a with
  | Binary((AluOp.Cmp | AluOp.Cmpm), _, _) | Unary(AluUnary.Tst, _) -> [||]
  | Binary(_, _, d) | Unary(_, d) | Mul(_, _, _, _, d) -> [| Acc.reg d |]
  | Max | Maxm -> [| B |]

let disjoint (xs: Reg array) (ys: Reg array) = xs |> Array.forall (fun x -> not (Array.contains x ys))

let probe : Encode.Env = { pc = 0; value = (fun _ -> Ok 0L); settled = fun _ -> true }

let encodes instr = (Encode.encode probe instr).IsOk

let wordsOf instr = match Encode.encode probe instr with Ok ws -> ws.Length | Error _ -> 99

/// Inside one word every source is read before anything is written. A move joins the ALU
/// instruction before it when it does not read what that instruction writes, and the one after
/// it when that instruction does not read what the move writes; neither may write what the other
/// writes. The merged instruction must encode in no more words than the two took.
let rec pack (items: Item array) =
  let facts (alu: Alu option) (moves: Move array) =
    let reads = Array.append (alu |> Option.map aluReads |> Option.defaultValue [||]) (Array.collect moveReads moves)
    let writes = Array.append (alu |> Option.map aluWrites |> Option.defaultValue [||]) (Array.collect moveWrites moves)
    reads, writes
  let tryMerge (first: Item) (second: Item) =
    match first, second with
    | Op(Par(a1, m1) as i1), Op(Par(a2, m2) as i2) when a1.IsNone || a2.IsNone ->
      let r1, w1 = facts a1 m1
      let r2, w2 = facts a2 m2
      let merged = Par(Option.orElse a2 a1, Array.append m1 m2)
      if disjoint w1 w2 && disjoint w1 r2 && encodes merged && wordsOf merged <= wordsOf i1 + wordsOf i2 - 1 then
        Some(Op merged)
      else None
    | _ -> None
  let out = ResizeArray<Item>()
  let mutable i = 0
  let mutable changed = false
  while i < items.Length do
    match (if i + 1 < items.Length then tryMerge items[i] items[i + 1] else None) with
    | Some merged ->
      out.Add merged
      changed <- true
      i <- i + 2
    | None ->
      out.Add items[i]
      i <- i + 1
  let result = out.ToArray()
  if changed then pack result else result

let addressUses instr =
  match instr with
  | Par(_, moves) -> moves |> Array.collect (fun m -> match m with Load(_, ea, _) | Store(_, _, ea) | Update ea -> eaRegs ea | _ -> [||])
  | Lua(ea, _) -> eaRegs ea
  | JumpEa(_, ea) -> eaRegs ea
  | _ -> [||]

let addressDefs instr =
  match instr with
  | Par(_, moves) -> moves |> Array.collect moveWrites |> Array.filter Reg.isAddress
  | Lua(_, d) -> [| d |]
  | _ -> [||]

/// A `nop` between an instruction loading rn/nn and one using it to form an address.
let hazards (items: Item array) =
  [| for i in 0 .. items.Length - 1 do
       items[i]
       match items[i], (if i + 1 < items.Length then Some items[i + 1] else None) with
       | Op x, Some(Op y) when not (disjoint (addressDefs x) (addressUses y)) -> Op Nop
       | _ -> () |]

/// Instructions and words of a program (loops counted once).
let count (items: Item array) =
  items |> Array.sumBy (fun i -> match i with Op _ -> 1 | _ -> 0),
  items |> Array.sumBy (fun i -> match i with Op x -> wordsOf x | Dc v -> v.Length | _ -> 0)
