#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/WojtusDiscord.ActivityArchiveService/WojtusDiscord.ActivityArchiveService.csproj", "src/WojtusDiscord.ActivityArchiveService/"]
RUN dotnet restore "src/WojtusDiscord.ActivityArchiveService/WojtusDiscord.ActivityArchiveService.csproj"
COPY . .
WORKDIR "/src/src/WojtusDiscord.ActivityArchiveService"
RUN dotnet build "WojtusDiscord.ActivityArchiveService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.ActivityArchiveService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.ActivityArchiveService.dll"]