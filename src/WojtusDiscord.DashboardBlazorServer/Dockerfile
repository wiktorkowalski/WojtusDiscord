#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["src/WojtusDiscord.DashboardBlazorServer/WojtusDiscord.DashboardBlazorServer.csproj", "src/WojtusDiscord.DashboardBlazorServer/"]
RUN dotnet restore "src/WojtusDiscord.DashboardBlazorServer/WojtusDiscord.DashboardBlazorServer.csproj"
COPY . .
WORKDIR "/src/src/WojtusDiscord.DashboardBlazorServer"
RUN dotnet build "WojtusDiscord.DashboardBlazorServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WojtusDiscord.DashboardBlazorServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WojtusDiscord.DashboardBlazorServer.dll"]