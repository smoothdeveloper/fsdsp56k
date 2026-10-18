/// The .NET host of the JSON reader (FromJson.fs): what the running program holds (module values, functions called on
/// constants, union cases) read by reflection on the assemblies already loaded, and the intrinsics, operators and zeros of the
/// kernel libraries registered from them.
module dsp56k.kernel.ReflectionHost

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open dsp56k.kernel.Kernel

let private flags = BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic

/// The .NET type of a full name from the loaded assemblies (a module's nested types are `Outer+Inner`).
let private clrType (full: string) : Type option =
  // the newest assembly first: a source compiled again (the explorer's) shadows its earlier version
  AppDomain.CurrentDomain.GetAssemblies()
  |> Array.rev
  |> Array.tryPick (fun a ->
    match a.GetType full with
    | null ->
      let rec nested (parts: string list) (prefix: string) =
        match parts with
        | [] -> None
        | p :: rest ->
          let candidate = if prefix = "" then p else prefix + "." + p
          match a.GetType candidate with
          | null -> nested rest candidate
          | t ->
            let rec dig (t: Type) (rest: string list) =
              match rest with
              | [] -> Some t
              | n :: more -> match t.GetNestedType(n, BindingFlags.Public ||| BindingFlags.NonPublic) with null -> None | nt -> dig nt more
            dig t rest
      nested (full.Split('.') |> List.ofArray) ""
    | t -> Some t)

let host: FromJson.Host =
  { getStatic =
      fun owner compiledName displayName ->
        match clrType owner with
        | None -> fail (displayName + ": its .NET code is not loaded")
        | Some t ->
          match t.GetProperty(compiledName, flags) with
          | null ->
            match t.GetProperty(displayName, flags) with
            | null -> fail (displayName + ": no .NET property to read")
            | p -> p.GetValue null
          | p -> p.GetValue null
    getInstance =
      fun instance name ->
        match instance.GetType().GetProperty(name, flags) with
        | null -> fail (name + ": not a property of " + instance.GetType().Name)
        | p -> p.GetValue instance
    invoke =
      fun owner compiledName types args ->
        match clrType owner with
        | None -> fail (compiledName + ": its .NET code is not loaded")
        | Some t ->
          let candidates = t.GetMethods flags |> Array.filter (fun mi -> mi.Name = compiledName && mi.GetParameters().Length = args.Length)
          let fits (mi: MethodInfo) =
            let ps = mi.GetParameters()
            types.Length = ps.Length && Array.forall2 (fun (n: string) (p: ParameterInfo) -> n = "" || n = p.ParameterType.FullName || p.ParameterType.IsGenericParameter) types ps
          let chosen = match candidates |> Array.filter fits with [||] -> candidates | c -> c
          match chosen with
          | [||] -> fail (compiledName + ": no .NET method to call")
          | cs ->
            try (Array.head cs).Invoke(null, args)
            with :? TargetInvocationException as e -> fail (compiledName + ": " + string e.InnerException)
    isCase =
      fun union case x ->
        match clrType union with
        | Some t -> (fst (FSharpValue.GetUnionFields(x, t))).Name = case
        | None -> fail "a union whose .NET type is not loaded" }

/// The kernel libraries' intrinsics, operators and zeros, registered where the reader finds them.
let register () = FromQuotation.ensureRegistered ()

/// Reads a kernel out of typed tree JSON text, on .NET.
let kernel (json: string) (moduleName: string) (name: string) (names: string * string * string) =
  register ()
  FromJson.kernelIn host (Json.parse json) moduleName name names
