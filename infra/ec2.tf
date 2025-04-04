module "autoscaling" {
  source  = "terraform-aws-modules/autoscaling/aws"
  version = "~> 8.1.0"

  name = "${local.project_name}-ecs-spot"

  min_size                  = 1
  max_size                  = 1
  desired_capacity          = 1
  wait_for_capacity_timeout = 0
  health_check_type         = "EC2"
  vpc_zone_identifier       = module.vpc.private_subnets

  # Launch template
  launch_template_name        = "${local.project_name}-ecs-spot"
  launch_template_description = "Launch template for ECS spot instances"
  update_default_version      = true

  # Use spot instances
  instance_market_options = {
    market_type = "spot"
    spot_options = {
      max_price = null # defaults to on-demand price
    }
  }

  image_id      = jsondecode(data.aws_ssm_parameter.ecs_optimized_ami.value)["image_id"]
  instance_type = "m8g.large"

  # IAM role & instance profile
  create_iam_instance_profile = true
  iam_role_name               = "${local.project_name}-ecs-instance"
  iam_role_description        = "ECS instance role"
  iam_role_policies = {
    AmazonEC2ContainerServiceforEC2Role = "arn:aws:iam::aws:policy/service-role/AmazonEC2ContainerServiceforEC2Role"
  }

  user_data = base64encode(<<-EOF
    #!/bin/bash
    echo "ECS_CLUSTER=${module.ecs.cluster_name}" >> /etc/ecs/ecs.config
    EOF
  )

  # Security group
  security_groups = [aws_security_group.ecs_sg.id]

  tags = {
    Environment = "dev"
  }
}

# Get the latest ECS-optimized AMI
data "aws_ssm_parameter" "ecs_optimized_ami" {
  name = "/aws/service/ecs/optimized-ami/amazon-linux-2/arm64/recommended"
}

# Security group for ECS instances
resource "aws_security_group" "ecs_sg" {
  name        = "${local.project_name}-ecs-instances"
  description = "Security group for ECS instances"
  vpc_id      = module.vpc.vpc_id

  ingress {
    from_port       = 0
    to_port         = 0
    protocol        = "-1"
    security_groups = [aws_security_group.alb_sg.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${local.project_name}-ecs-sg"
  }
}
