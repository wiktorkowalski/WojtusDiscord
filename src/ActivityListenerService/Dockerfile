FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/WojtusDiscord.ActivityListenerService/WojtusDiscord.ActivityListenerService.csproj", "src/WojtusDiscord.ActivityListenerService/"]
RUN dotnet restore "src/WojtusDiscord.ActivityListenerService/WojtusDiscord.ActivityListenerService.csproj"
COPY . .
WORKDIR "/src/src/WojtusDiscord.ActivityListenerService"
RUN dotnet build "WojtusDiscord.ActivityListenerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.ActivityListenerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.ActivityListenerService.dll"]
