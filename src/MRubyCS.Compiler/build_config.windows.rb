MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '3baee4b79cd4b6b83e6b72813a15a7b629455aff'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
end
