##
# Float ISO Test

if Object.const_defined?(:Float)

assert('Float', '15.2.9') do
  assert_equal Class, Float.class
end

assert('Float#+', '15.2.9.3.1') do
  a = 3.123456788 + 0.000000001
  b = 3.123456789 + 1

  assert_float(3.123456789, a)
  assert_float(4.123456789, b)

  assert_raise(TypeError){ 0.0+nil }
  assert_raise(TypeError){ 1.0+nil }
end

assert('Float#-', '15.2.9.3.2') do
  a = 3.123456790 - 0.000000001
  b = 5.123456789 - 1

  assert_float(3.123456789, a)
  assert_float(4.123456789, b)
end

assert('Float#*', '15.2.9.3.3') do
  a = 3.125 * 3.125
  b = 3.125 * 1

  assert_float(9.765625, a)
  assert_float(3.125   , b)
end

assert('Float#/', '15.2.9.3.4') do
  assert_float(1.0, 3.123456789 / 3.123456789)
  assert_float(3.123456789, 3.123456789 / 1)
  assert_float(2.875, -5.75 / -2.0)
  assert_float(-2.875, 5.75 / -2)
  assert_float(-2.875, -5.75 / 2.0)
  assert_float(Float::NAN, 0.0 / 0.0)
  assert_float(Float::NAN, -0.0 / -0.0)
  assert_float(Float::NAN, -0.0 / 0.0)
  assert_float(Float::NAN, Float::NAN / Float::NAN)
  assert_float(Float::NAN, Float::NAN / 0.0)
  assert_float(Float::NAN, Float::NAN / -0.0)
  assert_float(Float::NAN, Float::NAN / 2.0)
  assert_float(Float::NAN, Float::NAN / -2.0)
  assert_float(Float::NAN, 0.0 / Float::NAN)
  assert_float(Float::NAN, -0.0 / Float::NAN)
  assert_float(Float::NAN, 2.0 / Float::NAN)
  assert_float(Float::NAN, -2.0 / Float::NAN)
  assert_float(Float::NAN, Float::INFINITY / Float::INFINITY)
  assert_float(Float::NAN, -Float::INFINITY / Float::INFINITY)
  assert_float(Float::NAN, Float::INFINITY / -Float::INFINITY)
  assert_float(Float::NAN, -Float::INFINITY / -Float::INFINITY)
  assert_float(Float::INFINITY, 1.0 / 0.0)
  assert_float(Float::INFINITY, -1.0 / -0.0)
  assert_float(-Float::INFINITY, 1.0 / -0.0)
  assert_float(-Float::INFINITY, -1.0 / 0.0)
  assert_float(0.0, 1.0 / Float::INFINITY)
  assert_float(0.0, -1.0 / -Float::INFINITY)
  assert_float(-0.0, -1.0 / Float::INFINITY)
  assert_float(-0.0, 1.0 / -Float::INFINITY)
end
assert('Float#quo') do
  assert_float(1.0, 3.123456789.quo(3.123456789))
  assert_float(3.123456789, 3.123456789.quo(1))
  assert_float(2.875, -5.75.quo(-2.0))
  assert_float(-2.875, 5.75.quo(-2))
  assert_float(-2.875, -5.75.quo(2.0))
  assert_float(Float::NAN, 0.0.quo(0.0))
  assert_float(Float::NAN, -0.0.quo(-0.0))
  assert_float(Float::NAN, -0.0.quo(0.0))
  assert_float(Float::NAN, Float::NAN.quo(Float::NAN))
  assert_float(Float::NAN, Float::NAN.quo(0.0))
  assert_float(Float::NAN, Float::NAN.quo(-0.0))
  assert_float(Float::NAN, Float::NAN.quo(2.0))
  assert_float(Float::NAN, Float::NAN.quo(-2.0))
  assert_float(Float::NAN, 0.0.quo(Float::NAN))
  assert_float(Float::NAN, -0.0.quo(Float::NAN))
  assert_float(Float::NAN, 2.0.quo(Float::NAN))
  assert_float(Float::NAN, -2.0.quo(Float::NAN))
  assert_float(Float::NAN, Float::INFINITY.quo(Float::INFINITY))
  assert_float(Float::NAN, -Float::INFINITY.quo(Float::INFINITY))
  assert_float(Float::NAN, Float::INFINITY.quo(-Float::INFINITY))
  assert_float(Float::NAN, -Float::INFINITY.quo(-Float::INFINITY))
  assert_float(Float::INFINITY, 1.0.quo(0.0))
  assert_float(Float::INFINITY, -1.0.quo(-0.0))
  assert_float(-Float::INFINITY, 1.0.quo(-0.0))
  assert_float(-Float::INFINITY, -1.0.quo(0.0))
  assert_float(0.0, 1.0.quo(Float::INFINITY))
  assert_float(0.0, -1.0.quo(-Float::INFINITY))
  assert_float(-0.0, -1.0.quo(Float::INFINITY))
  assert_float(-0.0, 1.0.quo(-Float::INFINITY))
end

assert('Float#%', '15.2.9.3.5') do
  a = 3.125 % 3.125
  b = 3.125 % 1

  assert_float(0.0  , a)
  assert_float(0.125, b)
end

assert('Float#<=>', '15.2.9.3.6') do
  a = 3.125 <=> 3.123
  b = 3.125 <=> 3.125
  c = 3.125 <=> 3.126
  a2 = 3.125 <=> 3
  c2 = 3.125 <=> 4

  assert_equal( 1, a)
  assert_equal( 0, b)
  assert_equal(-1, c)
  assert_equal( 1, a2)
  assert_equal(-1, c2)
end

assert('Float#==', '15.2.9.3.7') do
  assert_true 3.1 == 3.1
  assert_false 3.1 == 3.2
end

assert('Float#ceil', '15.2.9.3.8') do
  a = 3.123456789.ceil
  b = 3.0.ceil
  c = -3.123456789.ceil
  d = -3.0.ceil

  assert_equal( 4, a)
  assert_equal( 3, b)
  assert_equal(-3, c)
  assert_equal(-3, d)
end

assert('Float#finite?', '15.2.9.3.9') do
  assert_predicate 3.123456789, :finite?
  assert_not_predicate 1.0 / 0.0, :finite?
end

assert('Float#floor', '15.2.9.3.10') do
  a = 3.123456789.floor
  b = 3.0.floor
  c = -3.123456789.floor
  d = -3.0.floor

  assert_equal( 3, a)
  assert_equal( 3, b)
  assert_equal(-4, c)
  assert_equal(-3, d)
end

assert('Float#infinite?', '15.2.9.3.11') do
  a = 3.123456789.infinite?
  b = (1.0 / 0.0).infinite?
  c = (-1.0 / 0.0).infinite?

  assert_nil a
  assert_equal( 1, b)
  assert_equal(-1, c)
end

assert('Float#round', '15.2.9.3.12') do
  a = 3.123456789.round
  b = 3.5.round
  c = 3.4999.round
  d = (-3.123456789).round
  e = (-3.5).round
  f = 12345.67.round(-1)
  g = 3.423456789.round(0)
  h = 3.423456789.round(1)
  i = 3.423456789.round(3)

  assert_equal(    3, a)
  assert_equal(    4, b)
  assert_equal(    3, c)
  assert_equal(   -3, d)
  assert_equal(   -4, e)
  assert_equal(12350, f)
  assert_equal(    3, g)
  assert_float(  3.4, h)
  assert_float(3.423, i)

  assert_equal(42.0, 42.0.round(307))
  assert_equal(1.0e307, 1.0e307.round(2))

  inf = 1.0/0.0
  assert_raise(FloatDomainError){ inf.round }
  assert_raise(FloatDomainError){ inf.round(-1) }
  assert_equal(inf, inf.round(1))
  nan = 0.0/0.0
  assert_raise(FloatDomainError){ nan.round }
  assert_raise(FloatDomainError){ nan.round(-1) }
  assert_predicate(nan.round(1), :nan?)
end

assert('Float#to_f', '15.2.9.3.13') do
  a = 3.123456789

  assert_float(a, a.to_f)
end

assert('Float#to_i', '15.2.9.3.14') do
  assert_equal(3, 3.123456789.to_i)
  assert_raise(FloatDomainError) { Float::INFINITY.to_i }
  assert_raise(FloatDomainError) { (-Float::INFINITY).to_i }
  assert_raise(FloatDomainError) { Float::NAN.to_i }
end

assert('Float#truncate', '15.2.9.3.15') do
  assert_equal( 3,  3.123456789.truncate)
  assert_equal(-3, -3.1.truncate)
end

assert('Float#divmod') do
  def check_floats(exp, act)
    assert_float exp[0], act[0]
    assert_float exp[1], act[1]
  end

  # Note: quotients are Float because mruby does not have Bignum.
  check_floats [ 0,  0.0],   0.0.divmod(1)
  check_floats [ 0,  1.1],   1.1.divmod(3)
  check_floats [ 3,  0.2],   3.2.divmod(1)
  check_floats [ 2,  6.3],  20.3.divmod(7)
  check_floats [-1,  1.6],  -3.4.divmod(5)
  check_floats [-2, -0.5],  25.5.divmod(-13)
  check_floats [ 1, -6.6], -13.6.divmod(-7)
  check_floats [ 3,  0.2],   9.8.divmod(3.2)
end

assert('Float#nan?') do
  assert_predicate(0.0/0.0, :nan?)
  assert_not_predicate(0.0, :nan?)
  assert_not_predicate(1.0/0.0, :nan?)
  assert_not_predicate(-1.0/0.0, :nan?)
end

# Ignore .udotnet formatter...
#assert('Float#to_s') do
#  uses_float = 4e38.infinite?  # enable MRB_USE_FLOAT32?
#
#  assert_equal("Infinity", Float::INFINITY.to_s)
#  assert_equal("-Infinity", (-Float::INFINITY).to_s)
#  assert_equal("NaN", Float::NAN.to_s)
#  assert_equal("0.0", 0.0.to_s)
#  assert_equal("-0.0", -0.0.to_s)
#  assert_equal("-3.25", -3.25.to_s)
#  assert_equal("50.0", 50.0.to_s)
#  assert_equal("0.0125", 0.0125.to_s)
#  assert_equal("-0.0125", -0.0125.to_s)
#  assert_equal("1.0e-10", 0.0000000001.to_s)
#  assert_equal("-1.0e-10", -0.0000000001.to_s)
#  assert_equal("1.0e+20", 1e20.to_s)
#  assert_equal("-1.0e+20", -1e20.to_s)
#  assert_equal("100000.0", 100000.0.to_s)
#  assert_equal("-100000.0", -100000.0.to_s)
#  if uses_float
#    assert_equal("1.0e+08", 100000000.0.to_s)
#    assert_equal("-1.0e+08", -100000000.0.to_s)
#    assert_equal("1.0e+07", 10000000.0.to_s)
#    assert_equal("-1.0e+07", -10000000.0.to_s)
#  else
#    assert_equal("1.0e+15", 1000000000000000.0.to_s)
#    assert_equal("-1.0e+15", -1000000000000000.0.to_s)
#    assert_equal("100000000000000.0", 100000000000000.0.to_s)
#    assert_equal("-100000000000000.0", -100000000000000.0.to_s)
#  end
#end

#assert('Float#inspect') do
#  assert_equal("-3.25", -3.25.inspect)
#  assert_equal("50.0", 50.0.inspect)
#end

assert('Float#eql?') do
  assert_operator(5.0, :eql?, 5.0)
  assert_not_operator(5.0, :eql?, 5)
  assert_not_operator(5.0, :eql?, "5.0")
end

assert('Float#abs') do
  f = 1.0
  assert_equal(1.0, f.abs)
  f = -1.0
  assert_equal(1.0, f.abs)
  f = 0.0
  assert_equal(0.0, f.abs)
  # abs(negative zero) should be positive zero
  f = -0.0
  assert_equal(0.0, f.abs)
end

end # const_defined?(:Float)