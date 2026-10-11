#r "nuget: DiffSharp-cpu, 1.0.7"

// Based on the DiffSharp quickstart: https://diffsharp.github.io/quickstart.html

open System.IO
open DiffSharp
open DiffSharp.Data
open DiffSharp.Model
open DiffSharp.Compose

// Keep the downloaded MNIST files and the saved image out of the repository
let workDir = Path.Combine(Path.GetTempPath(), "Fesh-MNIST")
Directory.CreateDirectory workDir |> ignore

// ---------------------------------------------------------------------------
// Datasets
// ---------------------------------------------------------------------------

// First ten images in MNIST training set
let dataset = MNIST(Path.Combine(workDir, "data"), train = true, transform = id, n = 10)

// Inspect a single image and label
let data, label = dataset[7]

// Save image to file
data.saveImage (Path.Combine(workDir, "test.png"))

// Inspect data as ASCII and show label
printfn "Data: %A\nLabel: %A" (data.toImageString ()) label

// ---------------------------------------------------------------------------
// Data loader
// ---------------------------------------------------------------------------

let loader = DataLoader(dataset, shuffle = true, batchSize = 6)
let batch, labels = loader.batch ()

printfn "%A\nLabels: %A" (batch.toImageString ()) labels

for epoch = 1 to 10 do
    for i, _data, _labels in loader.epoch () do
        printfn "Epoch %A, minibatch %A" epoch (i + 1)
        // Process the minibatch
        // ...

// ---------------------------------------------------------------------------
// Models, PyTorch style
// ---------------------------------------------------------------------------

// Define a model class inheriting the base
type Generator(nz: int) =
    inherit Model()
    let fc1 = Linear(nz, 256)
    let fc2 = Linear(256, 512)
    let fc3 = Linear(512, 1024)
    let fc4 = Linear(1024, 28 * 28)
    do base.addModel [ box fc1; box fc2; box fc3; box fc4 ]

    override _.forward(x) =
        x
        |> dsharp.view [ -1; nz ]
        |> fc1.forward
        |> dsharp.leakyRelu 0.2
        |> fc2.forward
        |> dsharp.leakyRelu 0.2
        |> fc3.forward
        |> dsharp.leakyRelu 0.2
        |> fc4.forward
        |> dsharp.tanh

// Define a model class inheriting the base
type Discriminator() =
    inherit Model()
    let fc1 = Linear(28 * 28, 1024)
    let fc2 = Linear(1024, 512)
    let fc3 = Linear(512, 256)
    let fc4 = Linear(256, 1)
    do base.addModel [ box fc1; box fc2; box fc3; box fc4 ]

    override _.forward(x) =
        x
        |> dsharp.view [ -1; 28 * 28 ]
        |> fc1.forward
        |> dsharp.leakyRelu 0.2
        |> dsharp.dropout 0.3
        |> fc2.forward
        |> dsharp.leakyRelu 0.2
        |> dsharp.dropout 0.3
        |> fc3.forward
        |> dsharp.leakyRelu 0.2
        |> dsharp.dropout 0.3
        |> fc4.forward
        |> dsharp.sigmoid

// Instantiate the defined classes
let nz = 128
let gen = Generator(nz)
let dis = Discriminator()

printfn "%s" (gen.summary ())
printfn "%s" (dis.summary ())

// ---------------------------------------------------------------------------
// Models, DiffSharp style
// ---------------------------------------------------------------------------

// Model as a composition of models and Tensor->Tensor functions
let generator =
    dsharp.view [ -1; nz ]
    --> Linear(nz, 256)
    --> dsharp.leakyRelu 0.2
    --> Linear(256, 512)
    --> dsharp.leakyRelu 0.2
    --> Linear(512, 1024)
    --> dsharp.leakyRelu 0.2
    --> Linear(1024, 28 * 28)
    --> dsharp.tanh

// Model as a composition of models and Tensor->Tensor functions
let discriminator =
    dsharp.view [ -1; 28 * 28 ]
    --> Linear(28 * 28, 1024)
    --> dsharp.leakyRelu 0.2
    --> dsharp.dropout 0.3
    --> Linear(1024, 512)
    --> dsharp.leakyRelu 0.2
    --> dsharp.dropout 0.3
    --> Linear(512, 256)
    --> dsharp.leakyRelu 0.2
    --> dsharp.dropout 0.3
    --> Linear(256, 1)
    --> dsharp.sigmoid

printfn "%s" (generator.summary ())
printfn "%s" (discriminator.summary ())
