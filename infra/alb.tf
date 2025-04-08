resource "aws_lb" "alb" {
  name               = "alb-${local.project_name}"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb_sg.id]
  subnets            = module.vpc.public_subnets
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.alb.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "fixed-response"
    
    fixed_response {
      content_type = "text/plain"
      message_body = "No routes matched"
      status_code  = "404"
    }
  }
}

# Dynamically create target groups for each service
resource "aws_lb_target_group" "service_tg" {
  for_each = local.services
  
  name        = "${each.value.name}-tg-${local.project_name}"
  port        = each.value.port
  protocol    = "HTTP"
  vpc_id      = module.vpc.vpc_id
  target_type = "ip"

  health_check {
    interval            = 30
    path                = each.value.health_check_path
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 2
    protocol            = "HTTP"
  }
}

# Create listener rules for each service
resource "aws_lb_listener_rule" "service_rules" {
  for_each = local.services
  
  listener_arn = aws_lb_listener.http.arn
  priority     = 100 + index(keys(local.services), each.key)

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.service_tg[each.key].arn
  }

  condition {
    host_header {
      values = [each.value.route53_record]
    }
  }
}

resource "aws_security_group" "alb_sg" {
  name        = "alb-sg-${local.project_name}"
  description = "Allow HTTP traffic to ALB"
  vpc_id      = module.vpc.vpc_id

  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}
