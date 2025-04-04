resource "aws_route53_record" "nginx_record" {
  zone_id = data.aws_route53_zone.aws.zone_id
  name    = "nginx.aws.wiktorkowalski.pl"
  type    = "A"

  alias {
    name                   = aws_lb.alb.dns_name
    zone_id                = aws_lb.alb.zone_id
    evaluate_target_health = true
  }
}

data "aws_route53_zone" "aws" {
  name = "aws.wiktorkowalski.pl"
}
