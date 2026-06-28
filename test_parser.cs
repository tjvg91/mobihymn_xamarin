using MobiHymn4.Utils;
foreach (var phrase in new[] { "4 5 6", "four five six", "3 2 1", "three two one", "456", "four 5 6" }) {
    var ok = VoiceHymnParser.TryParseHymnNumber(phrase, out var n);
    Console.WriteLine($"{phrase,-20} => {(ok ? n : "FAIL")}");
}
