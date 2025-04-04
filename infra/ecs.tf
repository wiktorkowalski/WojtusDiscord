module "ecs" {
  source  = "terraform-aws-modules/ecs/aws"
  version = "~> 5.12.0"  # Latest version as of March 2025

  cluster_name = "ecs-cluster-${local.project_name}"

  # Replace Fargate with EC2 spot instances
  autoscaling_capacity_providers = {
    spot_m8g = {
      auto_scaling_group_arn         = module.autoscaling.autoscaling_group_arn
      managed_termination_protection = "DISABLED"
      
      managed_scaling = {
        maximum_scaling_step_size = 1
        minimum_scaling_step_size = 1
        status                    = "ENABLED"
        target_capacity           = 100
      }

      default_capacity_provider_strategy = {
        weight = 100
      }
    }
  }

  # Task definition
  services = {
    web-service = {
      cpu    = 256
      memory = 512

      launch_type = "EC2"
      runtime_platform = {
        cpu_architecture = "ARM64"
        operating_system = "LINUX"
      }

      # Container definition(s)
      container_definitions = {
        nginx = {
          name      = "web-container"
          image     = "nginx:latest"
          cpu       = 256
          memory    = 512
          essential = true
          readonly_root_filesystem = false
          
          port_mappings = [
            {
              containerPort = 80
              hostPort      = 80
              protocol      = "tcp"
            }
          ]
        }
      }
      
      # Service settings
      desired_count = 1
      
      # Network configuration
      subnet_ids         = module.vpc.private_subnets
      security_group_ids = [aws_security_group.alb_sg.id]
      
      # Load balancer
      load_balancer = {
        service = {
          target_group_arn = aws_lb_target_group.nginx_tg.arn
          container_name   = "web-container"
          container_port   = 80
        }
      }

      depends_on = [aws_lb_listener.http]
    }
  }
}
