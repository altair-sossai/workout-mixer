# WorkoutMixer

O `WorkoutMixer` é um aplicativo desktop em WPF para montar trilhas de treino a partir de arquivos MP3. Ele permite organizar a ordem das músicas, visualizar a duração total, aplicar transições suaves entre as faixas e exportar um mix final em um único arquivo `.mp3`.

Além da mixagem de áudio, o projeto também permite montar uma linha de intensidade do treino com zonas configuráveis, duração por segmento e RPM, gerando um relatório textual com a sequência planejada.

## O que o projeto faz

- Importa uma ou mais faixas MP3.
- Permite reordenar e remover músicas da lista.
- Gera um mix final com `crossfade` entre as faixas.
- Exibe informações como duração, tamanho e forma de onda dos arquivos.
- Permite montar segmentos de intensidade do treino com zonas configuradas.
- Exporta um relatório `.txt` com a sequência de intensidades do treino.

## Tecnologias usadas

- `.NET 10`
- `WPF`
- `MahApps.Metro`
- `NAudio`
- `NAudio.Lame`
- `Microsoft.Extensions.Hosting` e `DependencyInjection`
- `Serilog`

## Configuração

As zonas de intensidade do gráfico ficam em [`src/appsettings.json`](/D:/Pessoal/WorkoutMixer/src/appsettings.json). É ali que você pode ajustar nomes, cores e valores máximos de cada zona.

## Como executar

Pré-requisitos:

- Windows
- SDK do `.NET 10`

Comandos:

```powershell
dotnet restore src/WorkoutMixer.csproj
dotnet run --project src/WorkoutMixer.csproj
```

## Fluxo de uso

1. Adicione os arquivos MP3.
2. Reordene as faixas conforme o treino desejado.
3. Monte os segmentos de intensidade no painel lateral.
4. Exporte o mix final em MP3.
5. Se quiser, exporte também o relatório de intensidades em `.txt`.

## Observação sobre o projeto

Este projeto foi feito utilizando vibecoding na maioria das vezes. A proposta aqui foi construir algo útil de forma iterativa, rápida e prática, refinando a aplicação conforme as necessidades foram surgindo.
