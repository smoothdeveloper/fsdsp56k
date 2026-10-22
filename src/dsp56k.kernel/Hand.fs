/// Helpers written twice ([<HandWritten>]): the F# body is the reference the .NET prototype runs and
/// the grade holds the program to, and the program runs a routine written by hand in the DSL. The
/// routines a front end found, by the key of the helper they stand for, and what the compiler checks
/// of a routine before it puts it in a program: the registers it changes, the workset words it
/// reaches, the system stack levels its DO loops take.
module dsp56k.kernel.Hand

open System
open System.Collections.Concurrent
open System.Reflection
open dsp56k

/// A hand routine: the helper it stands for, the value it is read from, its items (read when first
/// compiled).
type Routine =
  { helper : string
    name   : string
    items  : Lazy<Item array> }

/// The routines, by the key (Ir.Member.Key) of the helper each stands for. Front ends fill it.
let routines = ConcurrentDictionary<string, Routine>()

let register (key: string) (helper: string) (name: string) (read: unit -> Item array) =
  routines[key] <- { helper = helper; name = name; items = lazy (read ()) }

let find (key: string) =
  match routines.TryGetValue key with
  | true, r -> Some r
  | _ -> None

let private flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic

/// The items of the module value `name`, for the helper `helper` declared in `home`: a full name
/// (`workshop.hand.shape`, a nested module's too), or a bare one in `home`.
let resolve (home: Type) (helper: string) (name: string) : Item array =
  let candidates =
    match name.LastIndexOf '.' with
    | -1 -> [ home, name ]
    | i ->
      let owner, value = name.Substring(0, i), name.Substring(i + 1)
      // a module nested in another is Outer+Inner to reflection
      let parts = owner.Split '.'
      let spellings =
        [ for k in parts.Length .. -1 .. 1 ->
            String.Join(".", parts[.. k - 1]) + (if k < parts.Length then "+" + String.Join("+", parts[k ..]) else "") ]
      let assemblies = home.Assembly :: (AppDomain.CurrentDomain.GetAssemblies() |> Array.rev |> List.ofArray)
      [ for a in assemblies do
          for s in spellings do
            match a.GetType s with
            | null -> ()
            | t -> yield t, value ]
  let read (t: Type, value: string) =
    match t.GetProperty(value, flags) with
    | null -> None
    | p ->
      let v =
        try p.GetValue null
        with :? TargetInvocationException as e when not (isNull e.InnerException) ->
          Kernel.fail $"{helper}: [<HandWritten \"{name}\">]: building the routine failed: {(nonNull e.InnerException).Message}"
      match v with
      | :? (Item array) as items -> Some items
      | :? (Item list) as items -> Some(Array.ofList items)
      | _ -> Kernel.fail $"{helper}: [<HandWritten \"{name}\">] names {p.PropertyType.Name}, not DSL items (an Item array)"
  match candidates |> List.tryPick read with
  | Some items -> items
  | None -> Kernel.fail $"{helper}: [<HandWritten \"{name}\">]: no module value {name} (its full name, as workshop.hand.shape, or a value of the helper's module)"

/// The routine's items, read now.
let items (r: Routine) = r.items.Value

/// The labels the items define.
let labels (items: Item array) = items |> Array.choose (fun it -> match it with Label l -> Some l | _ -> None)

/// The items with each label they define renamed, and the branches, jumps and DO loops to it.
let relabel (rename: string -> string) (items: Item array) =
  let own = Set.ofArray (labels items)
  let op (o: Operand) = match o.expr with Sym s when own.Contains s -> { o with expr = Sym(rename s) } | _ -> o
  items |> Array.map (fun it ->
    match it with
    | Label l -> Label(rename l)
    | Op(Branch(k, t)) -> Op(Branch(k, op t))
    | Op(Jump(k, t)) -> Op(Jump(k, op t))
    | Op(Loop(k, c, e)) -> Op(Loop(k, c, op e))
    | Op(Forever(k, e)) -> Op(Forever(k, op e))
    | Op(BitBranch(k, b, t, d)) -> Op(BitBranch(k, b, t, op d))
    | it -> it)

/// The items with the loader's relocation mark (a label named after `tag`, then the marker) before
/// each instruction holding an absolute program address (a DO loop's end, a jump), as the compiler
/// marks its own: a package is loaded anywhere.
/// TODO: an address taken as data (`move #LABEL,r0`) is not seen; such a routine marks it itself
/// (`Label`, then `machinedrum.Abi.relocateMarker`).
let relocated (tag: string) (items: Item array) =
  let absolute it =
    match it with
    | Op(Loop _) | Op(Forever _) | Op(Jump _) | Op(BitBranch((Jclr | Jset | Jsclr | Jsset), _, _, _)) -> true
    | _ -> false
  [| for i in 0 .. items.Length - 1 do
       if absolute items[i] && (i = 0 || items[i - 1] <> machinedrum.Abi.relocateMarker) then
         yield Label $"{tag}_RELOC{i}"
         yield machinedrum.Abi.relocateMarker
       yield items[i] |]

/// The items without a last rts (written at a call, the code after the call follows them).
let withoutRts (items: Item array) =
  match items |> Array.tryFindIndexBack (fun it -> match it with Op _ -> true | _ -> false) with
  | Some i when items[i] = Op Rts -> Array.append items[.. i - 1] items[i + 1 ..]
  | _ -> items

/// The system stack levels the routine takes: two a DO loop, one a call it makes.
let depth (items: Item array) =
  let mutable around : string list = []
  let mutable deepest = 0
  for it in items do
    match it with
    | Op(Loop(_, _, { expr = Sym e })) | Op(Forever(_, { expr = Sym e })) ->
      around <- e :: around
      deepest <- max deepest (2 * around.Length)
    | Label l ->
      while (match around with e :: _ when e = l -> true | _ -> false) do around <- List.tail around
    | Op(Branch((Bsr | Bscc _), _)) | Op(Jump((Jsr | Jscc _), _)) | Op(BranchRn((Bsr | Bscc _), _)) | Op(JumpEa((Jsr | Jscc _), _)) ->
      deepest <- max deepest (2 * around.Length + 1)
    | _ -> ()
  deepest

/// What a routine does to registers and the workset: the registers it writes (as destinations), the
/// address registers its effective addresses step, and the workset words it reaches through r6.
type Touches =
  { writes  : Reg list
    steps   : Ea list
    workset : (Space * int) list }

let private eaOf (m: Move) =
  match m with
  | Load(sp, ea, _) | Store(_, sp, ea) -> Some(sp, ea)
  | Update ea -> Some(Space.X, ea)
  | _ -> None

let touches (items: Item array) =
  let writes, steps, workset = ResizeArray<Reg>(), ResizeArray<Ea>(), ResizeArray<Space * int>()
  let ea (sp: Space) (e: Ea) =
    match e with
    | PostDecN _ | PostIncN _ | PostDec _ | PostInc _ | PreDec _ -> steps.Add e
    | Disp(6, { expr = Num k }) -> workset.Add(sp, int k)
    | Ind 6 -> workset.Add(sp, 0)
    | _ -> ()
  for it in items do
    match it with
    | Op instr ->
      match instr with
      | Par(alu, moves) ->
        alu |> Option.iter (fun a -> writes.AddRange(dsp56k.dsl.Lower.aluWrites a))
        for m in moves do
          match m with
          | Load(_, _, r) | Copy(_, r) | Imm(_, r) -> writes.Add r
          | _ -> ()
          eaOf m |> Option.iter (fun (sp, e) -> ea sp e)
      | Lua(e, d) -> writes.Add d; ea Space.X e
      | Lra(_, d) -> writes.Add d
      | Norm(r, d) -> writes.Add(R r); writes.Add(Acc.reg d)
      | Tcc(_, alu, agu) ->
        alu |> Option.iter (fun (_, d) -> writes.Add(Acc.reg d))
        agu |> Option.iter (fun (_, d) -> writes.Add(R d))
      | CtrlImm(_, _, r) -> writes.Add r
      | Loop(_, FromMem(sp, e), _) | Rep(FromMem(sp, e)) -> ea sp e
      | BitOp(_, _, BitReg r) -> writes.Add r
      | BitOp(_, _, BitMem(sp, e)) | BitBranch(_, _, BitMem(sp, e), _) -> ea sp e
      | Vsl(_, _, e) -> ea Space.L e
      | Movep(false, _, _, MovepReg r) -> writes.Add r
      | Movep(_, _, _, MovepMem(sp, e)) -> ea sp e
      | _ -> ()
    | _ -> ()
  { writes = List.ofSeq writes; steps = List.ofSeq steps; workset = List.ofSeq workset }

/// The registers no routine may change: the workset's, the output's, and the control registers
/// (the condition codes excepted).
let private reserved (r: Reg) =
  match r with
  | R 6 | N 6 | M 6 | R 7 | N 7 | M 7 -> true
  | Sr | Omr | Sp | Ssh | Ssl | La | Lc | Ep | Vba | Sc | Sz | Mr | Eom -> true
  | _ -> false

/// Refused with a message when the routine, put where `free` address registers are free (r1..r5;
/// r0 is always the compiler's scratch), would change what the code around it keeps: a register
/// reserved, an address register holding a cursor, a local of the compiler's. `atCall`: written at
/// its calls, where an rts would leave the caller.
let check (helper: string) (r: Routine) (items: Item array) (free: int list) (layout: Kernel.Layout) (atCall: bool) =
  let what = $"{helper} (hand-written, {r.name})"
  let t = touches items
  let addressNumber (reg: Reg) = match reg with R k | N k | M k -> Some k | _ -> None
  for reg in t.writes do
    if reserved reg then Kernel.fail $"{what} changes {Print.reg reg}: r6, r7 and the control registers are the host's and the compiler's (output goes through (r7)+)"
    match addressNumber reg with
    | Some k when k >= 1 && k <= 5 && not (List.contains k free) ->
      Kernel.fail $"""{what} changes {Print.reg reg}, whose r{k} holds a cursor at this call (free here: {free |> List.map (sprintf "r%d") |> String.concat ", "}, and r0)"""
    | _ -> ()
  for e in t.steps do
    match e with
    | PostInc 7 -> ()
    | PostDecN 6 | PostIncN 6 | PostDec 6 | PostInc 6 | PreDec 6 -> Kernel.fail $"{what} steps r6: the workset's address is the host's"
    | PostDecN 7 | PostIncN 7 | PostDec 7 | PreDec 7 -> Kernel.fail $"{what} steps r7 other than by (r7)+: output goes forward, a word at a time"
    | PostDecN k | PostIncN k | PostDec k | PostInc k | PreDec k when k >= 1 && k <= 5 && not (List.contains k free) ->
      Kernel.fail $"{what} steps r{k}, which holds a cursor at this call"
    | _ -> ()
  for sp, k in t.workset do
    let local =
      match sp with
      | Space.X -> k >= layout.xWorkset
      | Space.Y -> k >= layout.yWorkset
      | _ -> k >= layout.xWorkset || k >= layout.yWorkset
    if local then
      Kernel.fail $"{what} reaches {(match sp with Space.X -> 'x' | Space.Y -> 'y' | _ -> 'l')}:(r6+{k}): the parameters and the state are the routine's to read and write, the words after them the compiler's locals"
  let ops = items |> Array.choose (fun it -> match it with Op i -> Some i | _ -> None)
  if atCall && (withoutRts items |> Array.exists (fun it -> it = Op Rts)) then
    Kernel.fail $"{what} returns from inside (an rts before its end): written at each call it cannot, mark the helper [<Subroutine>] too"
  if ops.Length = 0 then Kernel.fail $"{what} has no instructions"
