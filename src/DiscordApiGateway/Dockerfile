FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/DiscordApiGateway/DiscordApiGateway.csproj", "src/DiscordApiGateway/"]
RUN dotnet restore "src/DiscordApiGateway/DiscordApiGateway.csproj"
COPY . .
WORKDIR "/src/src/DiscordApiGateway"
RUN dotnet build "DiscordApiGateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DiscordApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DiscordApiGateway.dll"]
