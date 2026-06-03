FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/AgentsPromptsSkills.Web/AgentsPromptsSkills.Web.csproj"
RUN dotnet publish "src/AgentsPromptsSkills.Web/AgentsPromptsSkills.Web.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AgentsPromptsSkills.Web.dll"]
