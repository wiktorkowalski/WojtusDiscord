module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "5.19.0"

  name = "vpc-${local.project_name}"
  cidr = "10.0.0.0/16"

  azs             = ["eu-west-1a", "eu-west-1b", "eu-west-1c"]
  public_subnets  = ["10.0.2.0/24", "10.0.4.0/24", "10.0.6.0/24"]
  private_subnets = ["10.0.1.0/24", "10.0.3.0/24", "10.0.5.0/24"]

  database_subnets                                            = ["10.0.7.0/24", "10.0.9.0/24", "10.0.11.0/24"]
  database_subnet_enable_resource_name_dns_a_record_on_launch = true
  create_database_subnet_group                                = true

  enable_nat_gateway     = false
  single_nat_gateway     = false
  one_nat_gateway_per_az = false
  enable_vpn_gateway     = false

  default_vpc_enable_dns_hostnames = true
  default_vpc_enable_dns_support   = true
  enable_dns_support               = true
  enable_dns_hostnames             = true
}
