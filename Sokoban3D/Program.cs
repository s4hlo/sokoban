using System;
using Serilog;

// Rede de segurança de crash: o Serilog é configurado dentro do Game1 (construtor), então
// exceções ANTES disso caem no Console.Error abaixo; depois disso, viram Log.Fatal e vão parar
// no arquivo logs/sokoban-AAAAMMDD.log. Qualquer exceção não-tratada — inclusive de threads de
// fundo — é gravada aqui antes do processo morrer, pra o motivo do crash nunca se perder.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex)
        Log.Fatal(ex, "Crash não-tratado — o processo vai encerrar");
    else
        Log.Fatal("Crash não-tratado (objeto não-Exception): {Obj}", e.ExceptionObject);
    Log.CloseAndFlush();
};

try
{
    using var game = new Sokoban3D.Game1();
    game.Run();
}
catch (Exception ex)
{
    // Captura o caminho síncrono (o comum): tudo que estourar no Run() vem parar aqui e é
    // gravado antes de propagar/encerrar. Se o logger nem chegou a existir, ainda sobra o stderr.
    Log.Fatal(ex, "Crash não-tratado no laço principal — o jogo vai encerrar");
    Console.Error.WriteLine(ex);
    throw;
}
finally
{
    // Garante o flush do arquivo de log num encerramento normal (o sink de arquivo bufferiza).
    Log.CloseAndFlush();
}
