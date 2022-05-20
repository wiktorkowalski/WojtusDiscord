#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
LABEL org.opencontainers.image.source https://github.com/wiktorkowalski/wojtusdiscord

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["WojtusDiscord.ArchiveService.csproj", "WojtusDiscord.ArchiveService/"]
RUN dotnet restore "WojtusDiscord.ArchiveService/WojtusDiscord.ArchiveService.csproj"
COPY . "/src/WojtusDiscord.ArchiveService/"
WORKDIR "/src/WojtusDiscord.ArchiveService"
RUN dotnet build "WojtusDiscord.ArchiveService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.ArchiveService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.ArchiveService.dll"]