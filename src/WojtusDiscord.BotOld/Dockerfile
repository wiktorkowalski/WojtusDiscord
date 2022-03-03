#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["WojtusDiscord.Bot.csproj", "WojtusDiscord.Bot/"]
COPY ["nuget.config", "WojtusDiscord.Bot/"]
RUN dotnet restore --verbosity n --configfile "WojtusDiscord.Bot/nuget.config" "WojtusDiscord.Bot/WojtusDiscord.Bot.csproj"
COPY . "/src/WojtusDiscord.Bot/"
WORKDIR "/src/WojtusDiscord.Bot"
RUN dotnet build "WojtusDiscord.Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.Bot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.Bot.dll"]