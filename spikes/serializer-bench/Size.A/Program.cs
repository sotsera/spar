using Spar.Generated;

// (A) only: parse and write a Note and a Note[] with the generated serializers.
var note = BenchContractCodec.ParseSaveAsync("""{"text":"t","tags":["a"],"parent":null,"score":1.5,"pinned":true,"version":2}"""u8);
var notes = BenchContractCodec.ParseSaveManyAsync("""[{"text":"t","tags":[],"parent":null,"score":1,"pinned":false,"version":null}]"""u8);
Console.WriteLine(BenchContractCodec.SerializeSaveAsync(note).Length + BenchContractCodec.SerializeSaveManyAsync(notes).Length);
