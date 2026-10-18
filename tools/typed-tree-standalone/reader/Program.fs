// dotnet run --project dotnet.fsproj -- tree.json Module.Name kernel-name init trig render   (prints what ReaderShow.show gives)
[<EntryPoint>]
let main argv =
  printfn "%s" (ReaderShow.show (System.IO.File.ReadAllText argv[0]) argv[1] argv[2] argv[3] argv[4] argv[5])
  0
