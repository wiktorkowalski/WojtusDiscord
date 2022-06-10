#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
LABEL org.opencontainers.image.source https://github.com/wiktorkowalski/wojtusdiscord

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["WojtusDiscord.TechDealsService.csproj", "WojtusDiscord.TechDealsService/"]
RUN dotnet restore "WojtusDiscord.TechDealsService/WojtusDiscord.TechDealsService.csproj"
COPY . "/src/WojtusDiscord.TechDealsService/"
WORKDIR "/src/WojtusDiscord.TechDealsService"
RUN dotnet build "WojtusDiscord.TechDealsService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.TechDealsService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.TechDealsService.dll"]