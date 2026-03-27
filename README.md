# WorkoutMixer

`WorkoutMixer` is a WPF desktop application for building workout playlists from MP3 files. It lets you organize track order, view total duration, apply smooth transitions between songs, and export the final result as a single `.mp3` file.

In addition to audio mixing, the project also lets you define a workout intensity timeline with configurable zones, segment duration, and RPM, then export a text report with the planned sequence.

## What the project does

- Imports one or more MP3 tracks.
- Lets you reorder and remove songs from the list.
- Generates a final mix with crossfade transitions between tracks.
- Displays information such as duration, file size, and waveform data.
- Lets you create workout intensity segments using configured zones.
- Exports a `.txt` report with the workout intensity sequence.

## Technologies used

- `.NET 10`
- `WPF`
- `MahApps.Metro`
- `NAudio`
- `NAudio.Lame`
- `Microsoft.Extensions.Hosting` and `DependencyInjection`
- `Serilog`

## Configuration

The chart intensity zones are defined in [`src/appsettings.json`](/D:/Pessoal/WorkoutMixer/src/appsettings.json). You can adjust each zone's name, color, and maximum value there.

## How to run

Requirements:

- Windows
- `.NET 10` SDK

Commands:

```powershell
dotnet restore src/WorkoutMixer.csproj
dotnet run --project src/WorkoutMixer.csproj
```

## Usage flow

1. Add the MP3 files.
2. Reorder the tracks to match the desired workout flow.
3. Build the intensity segments in the side panel.
4. Export the final MP3 mix.
5. Optionally export the intensity report as a `.txt` file.

## Project note

This project was built using vibecoding most of the time. The idea was to create something useful in an iterative, fast, and practical way, refining the application as new needs came up.
