locals {
  project_name = "wojtus-discord"
  # cluster_name = "ecs-cluster-${project_name}"
  
  # Load services configuration from JSON file
  services = jsondecode(file("${path.module}/services.json")).services
}