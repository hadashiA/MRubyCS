MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '71749bc54a710ac4567130ebd1609c331ba60e7a'
  conf.gem './mrbgems/mrubycs-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
end
